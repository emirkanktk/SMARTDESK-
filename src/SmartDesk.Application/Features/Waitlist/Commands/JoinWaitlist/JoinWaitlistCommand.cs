using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Waitlist.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Waitlist.Commands.JoinWaitlist;

// "Book it for me if it frees up" (ADR-0043): the caller waits for a resource at a time that's
// taken. The times are as for a booking.
public sealed record JoinWaitlistCommand(Guid ResourceId, DateTime StartDateTime, DateTime EndDateTime)
    : IRequest<WaitlistEntryDto>;

public sealed class JoinWaitlistCommandValidator : AbstractValidator<JoinWaitlistCommand>
{
    public JoinWaitlistCommandValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.ResourceId).NotEmpty();

        RuleFor(x => x.StartDateTime)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(start => start.AsUtc() >= timeProvider.GetUtcNow().UtcDateTime - ReservationRules.PastStartGrace)
            .WithMessage(_ => Messages.Reservations_StartInPast)
            .Must(start => start.AsUtc() <= timeProvider.GetUtcNow().UtcDateTime + ReservationRules.MaxAdvance)
            .WithMessage(_ => string.Format(Messages.Reservations_TooFarAhead, ReservationRules.MaxAdvanceDays));

        RuleFor(x => x.EndDateTime)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must((command, end) => end.AsUtc() > command.StartDateTime.AsUtc())
            .WithMessage(_ => Messages.Validation_EndAfterStart)
            .Must((command, end) => end.AsUtc() - command.StartDateTime.AsUtc() <= ReservationRules.MaxDuration)
            .WithMessage(_ => string.Format(Messages.Reservations_MaxDuration, ReservationRules.MaxDuration.TotalDays));
    }
}

public sealed class JoinWaitlistCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    TimeProvider timeProvider,
    BookingPolicy policy) : IRequestHandler<JoinWaitlistCommand, WaitlistEntryDto>
{
    public async Task<WaitlistEntryDto> Handle(JoinWaitlistCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var start = request.StartDateTime.AsUtc();
        var end = request.EndDateTime.AsUtc();
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // The caller's to book and in use, as for booking it: 404 or 409 otherwise.
        var resource = await BookingGuard.EnsureResourceBookableAsync(db, currentUser, request.ResourceId, cancellationToken);

        // Only what they could book is worth waiting for: its kind's rules, and no more of the
        // kind than they may have (ADR-0046).
        await policy.EnsureAllowedAsync(new BookingClaim(userId, request.ResourceId, start, end), cancellationToken);

        // A freed time is booked for whoever waited, with nobody there to approve it (ADR-0049):
        // a kind that waits for an Admin's approval has no waiting list.
        if (await policy.ApprovingKindAsync(request.ResourceId, currentUser.IsInRole(Roles.Admin), cancellationToken) is { } kind)
        {
            throw new ConflictException(string.Format(Messages.Approval_NoWaitingList, kind))
            {
                Code = ConflictCodes.ApprovalRequired,
            };
        }

        // Nothing to wait for when it's free.
        var conflicts = await BookingGuard.FindConflictsAsync(
            db, request.ResourceId, resource.Name, [new Occurrence(start, end)], null, cancellationToken);
        if (conflicts[0] is null)
        {
            throw new ConflictException(string.Format(Messages.Waitlist_Free, resource.Name))
            {
                Code = ConflictCodes.WaitlistSlotFree,
            };
        }

        var ownBooking = await db.Reservations
            .Where(r => r.ResourceId == request.ResourceId && r.UserId == userId)
            .AnyAsync(ReservationOverlap.TakenOverlapping(start, end), cancellationToken);
        if (ownBooking)
        {
            throw new ConflictException(string.Format(Messages.Waitlist_BookedAlready, resource.Name));
        }

        var waiting = await db.WaitlistEntries
            .AsNoTracking()
            .Where(e => e.UserId == userId && e.EndDateTime > now)
            .Select(e => new { e.ResourceId, e.StartDateTime, e.EndDateTime })
            .ToListAsync(cancellationToken);
        if (waiting.Any(e => e.ResourceId == request.ResourceId && ReservationOverlap.Overlaps(e.StartDateTime, e.EndDateTime, start, end)))
        {
            throw new ConflictException(string.Format(Messages.Waitlist_AlreadyWaiting, resource.Name))
            {
                Code = ConflictCodes.WaitlistAlreadyWaiting,
            };
        }

        if (waiting.Count >= WaitlistRules.MaxPerUser)
        {
            throw new ConflictException(string.Format(Messages.Waitlist_TooMany, WaitlistRules.MaxPerUser));
        }

        var entry = new WaitlistEntry
        {
            Id = Guid.CreateVersion7(),
            ResourceId = request.ResourceId,
            UserId = userId,
            StartDateTime = start,
            EndDateTime = end,
            CreatedAt = now,
        };
        db.WaitlistEntries.Add(entry);
        await db.SaveChangesAsync(cancellationToken);

        return await WaitlistReader
            .Read(db, db.WaitlistEntries.AsNoTracking().Where(e => e.Id == entry.Id), now)
            .SingleAsync(cancellationToken);
    }
}
