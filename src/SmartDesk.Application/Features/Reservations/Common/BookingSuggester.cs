using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Reservations.Common;

// What a 409 for a booking that can't be made offers instead (ADR-0042), as its `suggestions`:
// the nearest free times on the same resource, and other resources of its kind, big enough and
// free at the time asked for. Only what the kind's rules allow (ADR-0046).
public sealed record BookingSuggestions(IReadOnlyList<SuggestedTime> Times, IReadOnlyList<SuggestedResource> Resources)
{
    public static readonly BookingSuggestions None = new([], []);
}

public sealed record SuggestedTime(DateTime Start, DateTime End);

public sealed record SuggestedResource(Guid Id, string Name, int Capacity, string Location);

public sealed class BookingSuggester(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    BookingPolicy policy)
{
    // A few to choose from at a glance, not a list to read through.
    public const int MaxTimes = 3;
    public const int MaxResources = 4;

    // How far either side of the time asked for free times are looked for.
    public static readonly TimeSpan Around = TimeSpan.FromDays(1);

    // `sameResource`: whether the resource asked for can be booked at another time; not when
    // it's out of use altogether.
    public async Task<BookingSuggestions> SuggestAsync(IBookingRequest request, bool sameResource, CancellationToken cancellationToken)
    {
        var start = request.StartDateTime.AsUtc();
        var end = request.EndDateTime.AsUtc();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // A booking under way can only run longer: another time or resource is no answer.
        if (request.MovingReservationId is { } moving
            && await db.Reservations.AnyAsync(r => r.Id == moving && r.StartDateTime <= now, cancellationToken))
        {
            return BookingSuggestions.None;
        }

        var asked = await db.Resources
            .AsNoTracking()
            .UsableBy(currentUser)
            .Where(r => r.Id == request.ResourceId)
            .Select(r => new { r.ResourceTypeId, r.Capacity })
            .SingleOrDefaultAsync(cancellationToken);
        if (asked is null)
        {
            return BookingSuggestions.None;
        }

        var limits = await policy.ForResourceAsync(request.ResourceId, cancellationToken);
        var times = sameResource && limits is not null ? await FreeTimesAsync(request, limits, start, end, now, cancellationToken) : [];

        // A time its kind's rules don't allow is no better on another resource of the kind.
        var resources = limits is not null && !limits.TimeProblems(start, end, now).Any()
            ? await OtherResourcesAsync(request, asked.ResourceTypeId, asked.Capacity, start, end, cancellationToken)
            : [];
        return new BookingSuggestions(times, resources);
    }

    private async Task<IReadOnlyList<SuggestedTime>> FreeTimesAsync(
        IBookingRequest request, BookingLimits limits, DateTime start, DateTime end, DateTime now, CancellationToken cancellationToken)
    {
        var from = start - Around;
        var to = end + Around;
        var bookings = await db.Reservations
            .AsNoTracking()
            .Where(r => r.ResourceId == request.ResourceId && r.Id != request.MovingReservationId)
            .Where(ReservationOverlap.ConfirmedOverlapping(from, to))
            .Select(r => new { r.StartDateTime, r.EndDateTime })
            .ToListAsync(cancellationToken);
        var blocks = await db.ResourceBlocks
            .AsNoTracking()
            .Where(b => b.ResourceId == request.ResourceId)
            .Where(ResourceBlockOverlap.Overlapping(from, to))
            .Select(b => new { b.StartDateTime, b.EndDateTime })
            .ToListAsync(cancellationToken);

        // For a kind kept to office hours, the hours the office is closed are as good as taken.
        var busy = bookings
            .Select(b => (b.StartDateTime.AsUtc(), b.EndDateTime.AsUtc()))
            .Concat(blocks.Select(b => (b.StartDateTime.AsUtc(), b.EndDateTime.AsUtc())))
            .Concat(limits.OfficeHoursOnly ? limits.Office.ClosedBetween(from, to) : []);
        return FreeTimes
            .Nearest(busy, start, end - start, from, to, earliest: now, latest: limits.LatestStart(now), MaxTimes)
            .Select(time => new SuggestedTime(time.Start, time.End))
            .ToList();
    }

    private async Task<IReadOnlyList<SuggestedResource>> OtherResourcesAsync(
        IBookingRequest request, Guid typeId, int askedCapacity, DateTime start, DateTime end, CancellationToken cancellationToken)
    {
        // The owner and everyone invited.
        var people = 1 + (request.AttendeeIds ?? []).Distinct().Count();
        var taken = db.Reservations
            .Where(r => r.Id != request.MovingReservationId)
            .Where(ReservationOverlap.ConfirmedOverlapping(start, end))
            .Select(r => r.ResourceId);
        var blocked = db.ResourceBlocks
            .Where(ResourceBlockOverlap.Overlapping(start, end))
            .Select(b => b.ResourceId);
        var free = await db.Resources
            .AsNoTracking()
            .UsableBy(currentUser)
            .Where(r => r.Id != request.ResourceId && r.ResourceTypeId == typeId && r.Status == ResourceStatus.Active)
            .Where(r => r.Capacity >= people)
            .Where(r => !taken.Contains(r.Id) && !blocked.Contains(r.Id))
            .Select(r => new SuggestedResource(r.Id, r.Name, r.Capacity, r.Location))
            .ToListAsync(cancellationToken);

        // Those as big as the one asked for, the smallest first; then the smaller ones, the
        // biggest first.
        return free
            .OrderBy(r => r.Capacity >= askedCapacity ? 0 : 1)
            .ThenBy(r => r.Capacity >= askedCapacity ? r.Capacity : -r.Capacity)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResources)
            .ToList();
    }
}
