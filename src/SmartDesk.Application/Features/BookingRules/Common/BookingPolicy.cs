using FluentValidation;
using FluentValidation.Results;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.BookingRules.Common;

// The one place the rules for what a booking is for are applied (ADR-0046): how long it may
// last, how far ahead it may start, whether it keeps to office hours, and how many one person
// may have - as an Admin sets them for each kind of resource. Every way of booking asks here:
// booking, moving, a series, running longer, the waiting list; and so does everything that
// offers times: the search, the suggestions, the extension options. The requests' validators
// check the rules for every booking (ReservationRules) before any of this, as they always have.
public sealed class BookingPolicy(IApplicationDbContext db, TimeProvider timeProvider)
{
    // The rules for booking a resource; null when there's no such resource.
    public async Task<BookingLimits?> ForResourceAsync(Guid resourceId, CancellationToken cancellationToken)
    {
        var type = await db.Resources
            .AsNoTracking()
            .Where(r => r.Id == resourceId)
            .Select(r => r.ResourceType)
            .SingleOrDefaultAsync(cancellationToken);
        return type is null ? null : BookingLimits.Of(type, await OfficeAsync(cancellationToken));
    }

    // The rules for each kind of resource, by its id.
    public async Task<IReadOnlyDictionary<Guid, BookingLimits>> ForEveryKindAsync(CancellationToken cancellationToken)
    {
        var office = await OfficeAsync(cancellationToken);
        var types = await db.ResourceTypes.AsNoTracking().ToListAsync(cancellationToken);
        return types.ToDictionary(t => t.Id, t => BookingLimits.Of(t, office));
    }

    // A booking, or a booking moved (ADR-0046): 400 when its time breaks its kind's rules, 409
    // (booking-limit) when its owner already has as many of the kind as allowed. Call it inside
    // the booking's transaction, after the resource's lock: when it counts, it takes the
    // owner's lock (ADR-0011), so two of their bookings can't both pass.
    public async Task EnsureAllowedAsync(BookingClaim claim, CancellationToken cancellationToken)
    {
        if (await RefusalAsync(claim, cancellationToken) is { } refusal)
        {
            throw refusal;
        }
    }

    // The same as a yes or no, for a booking nobody is there to be told about: the waiting list's.
    public async Task<bool> AllowsAsync(BookingClaim claim, CancellationToken cancellationToken) =>
        await RefusalAsync(claim, cancellationToken) is null;

    // A series' dates (ADR-0035): the first as for any booking and the last no further ahead
    // than the kind allows, or 400. Then, for each date, why it's outside office hours, to be
    // skipped like a date that isn't free; null for those that aren't.
    public async Task<IReadOnlyList<SlotConflict?>> CheckSeriesAsync(
        Guid resourceId, IReadOnlyList<Occurrence> dates, CancellationToken cancellationToken)
    {
        var limits = await ForResourceAsync(resourceId, cancellationToken);
        if (limits is null || dates.Count == 0)
        {
            return dates.Select(_ => (SlotConflict?)null).ToList();
        }

        var now = Now;
        var problems = limits.TimeProblems(dates[0].Start, dates[0].End, now)
            .Select(p => new ValidationFailure(p.Property, p.Message))
            .ToList();
        if (limits.SeriesReachProblem(dates[^1].Start, now) is { } reach)
        {
            problems.Add(new ValidationFailure(BookingLimits.UntilProperty, reach));
        }

        if (problems.Count > 0)
        {
            throw new ValidationException(problems);
        }

        var outside = new SlotConflict(
            ConflictCodes.OutsideOfficeHours, string.Format(Messages.Rules_DateOutsideOfficeHours, limits.Office.Describe()));
        return dates
            .Select(date => limits.OfficeHoursOnly && !limits.Office.Covers(date.Start, date.End) ? outside : null)
            .ToList();
    }

    // Booking `count` dates of a series: 409 (booking-limit) when its owner would have more of
    // the kind than allowed. Inside the transaction, after the resource's lock, as above.
    public async Task EnsureSeriesWithinLimitAsync(Guid ownerId, Guid resourceId, int count, CancellationToken cancellationToken)
    {
        var limits = await ForResourceAsync(resourceId, cancellationToken);
        if (limits?.MaxActivePerUser is not { } limit)
        {
            return;
        }

        var wouldHave = await UpcomingAsync(limits, ownerId, cancellationToken) + count;
        if (wouldHave > limit)
        {
            throw new ConflictException(limits.SeriesOverLimit(wouldHave)) { Code = ConflictCodes.BookingLimit };
        }
    }

    // How late a booking of the resource that starts at `start` may run: for the extension
    // options (ADR-0033). Unknown resources have no rules of their own here.
    public async Task<DateTime> LatestEndAsync(Guid resourceId, DateTime start, CancellationToken cancellationToken) =>
        (await ForResourceAsync(resourceId, cancellationToken))?.LatestEnd(start) ?? start + ReservationRules.MaxDuration;

    // A booking running on to `end` (ADR-0033): 409 with the rule it would break.
    public async Task EnsureCanRunOnAsync(Guid resourceId, DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        var limits = await ForResourceAsync(resourceId, cancellationToken);
        if (limits?.RunningOnProblem(start, end) is { } problem)
        {
            throw new ConflictException(problem);
        }
    }

    // When a booking of the resource that the caller makes waits for an Admin's approval
    // (ADR-0049) - its kind asks for it, and the caller isn't an Admin - the kind as the
    // request's language names it, to say so; null when it doesn't.
    public async Task<string?> ApprovingKindAsync(Guid resourceId, bool callerIsAdmin, CancellationToken cancellationToken) =>
        !callerIsAdmin && await ForResourceAsync(resourceId, cancellationToken) is { RequiresApproval: true } limits
            ? limits.Kind
            : null;

    // Whether a booking of the resource from `start` that the caller makes, or moves, waits for
    // an Admin's approval. One that does can't start right away (400 on the start): an Admin
    // needs the time to approve it.
    public async Task<bool> NeedsApprovalAsync(Guid resourceId, DateTime start, bool callerIsAdmin, CancellationToken cancellationToken)
    {
        if (await ApprovingKindAsync(resourceId, callerIsAdmin, cancellationToken) is not { } kind)
        {
            return false;
        }

        if (start <= Now)
        {
            throw new ValidationException(
                [new ValidationFailure(BookingLimits.StartProperty, string.Format(Messages.Approval_NotRightAway, kind))]);
        }

        return true;
    }

    // A series (ADR-0035) of a kind that waits for an Admin's approval: 400 on the resource,
    // unless an Admin books it. Each date is asked for on its own.
    public async Task EnsureCanRepeatAsync(Guid resourceId, bool callerIsAdmin, CancellationToken cancellationToken)
    {
        if (await ApprovingKindAsync(resourceId, callerIsAdmin, cancellationToken) is { } kind)
        {
            throw new ValidationException(
                [new ValidationFailure(ResourceProperty, string.Format(Messages.Approval_CantRepeat, kind))]);
        }
    }

    private const string ResourceProperty = "ResourceId";

    // The kinds of resource whose rules let [start, end) be booked, for the search: what can't
    // be booked isn't available.
    public async Task<IReadOnlyList<Guid>> KindsBookableAsync(DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        var now = Now;
        return (await ForEveryKindAsync(cancellationToken)).Values
            .Where(limits => !limits.TimeProblems(start, end, now).Any())
            .Select(limits => limits.TypeId)
            .ToList();
    }

    private async Task<Exception?> RefusalAsync(BookingClaim claim, CancellationToken cancellationToken)
    {
        // No such resource: the booking's own checks say so (BookingGuard).
        var limits = await ForResourceAsync(claim.ResourceId, cancellationToken);
        if (limits is null)
        {
            return null;
        }

        var problems = limits.TimeProblems(claim.Start, claim.End, Now)
            .Select(p => new ValidationFailure(p.Property, p.Message))
            .ToList();
        if (problems.Count > 0)
        {
            return new ValidationException(problems);
        }

        if (limits.MaxActivePerUser is not { } limit)
        {
            return null;
        }

        // A booking moved within its kind is still one of them, not one more.
        if (claim.MovingReservationId is { } moving
            && await db.Reservations.AnyAsync(r => r.Id == moving && r.Resource.ResourceTypeId == limits.TypeId, cancellationToken))
        {
            return null;
        }

        return await UpcomingAsync(limits, claim.OwnerId, cancellationToken) >= limit
            ? new ConflictException(limits.LimitReached) { Code = ConflictCodes.BookingLimit }
            : null;
    }

    // The owner's bookings of the kind that haven't ended, those waiting for approval too
    // (ADR-0049), counted under their lock so the count holds until the transaction ends.
    private async Task<int> UpcomingAsync(BookingLimits limits, Guid ownerId, CancellationToken cancellationToken)
    {
        await db.LockBookerAsync(ownerId, cancellationToken);
        var now = Now;
        return await db.Reservations.CountAsync(
            r => r.UserId == ownerId
                && r.Status != ReservationStatus.Cancelled
                && r.EndDateTime > now
                && r.Resource.ResourceTypeId == limits.TypeId,
            cancellationToken);
    }

    private async Task<OfficeWeek> OfficeAsync(CancellationToken cancellationToken) =>
        OfficeWeek.Of(await db.OfficeHours.AsNoTracking().SingleAsync(cancellationToken));

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;
}

// What a booking claims, for the policy: whose it is - the owner's, even when an Admin makes or
// moves it - which resource, when; and the booking it moves, when it's a move.
public sealed record BookingClaim(Guid OwnerId, Guid ResourceId, DateTime Start, DateTime End, Guid? MovingReservationId = null);
