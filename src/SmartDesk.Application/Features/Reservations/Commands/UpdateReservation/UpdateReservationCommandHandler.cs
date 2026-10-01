using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;

public sealed class UpdateReservationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier,
    TimeProvider timeProvider,
    MeetingInvitations invitations) : IRequestHandler<UpdateReservationCommand, ReservationDto>
{
    public async Task<ReservationDto> Handle(UpdateReservationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var start = request.StartDateTime.AsUtc();
        var end = request.EndDateTime.AsUtc();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        bool changed;
        List<InboxMessage> messages = [];

        // Same concurrency model as create (ADR-0011), plus a lock on the reservation itself so
        // a concurrent cancel or second update of it waits for this one.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockReservationAsync(request.Id, cancellationToken);

            var reservation = await db.Reservations.SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
                ?? throw new NotFoundException(nameof(Reservation), request.Id);

            // Business rule 4: the owner, or an Admin managing reservations.
            if (reservation.UserId != userId && !currentUser.IsInRole(Roles.Admin))
            {
                throw new ForbiddenAccessException(Messages.Reservations_ChangeOwnOnly);
            }

            if (reservation.Status == ReservationStatus.Cancelled)
            {
                throw new ConflictException(Messages.Reservations_CancelledCantChange);
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (reservation.EndDateTime <= now)
            {
                throw new ConflictException(Messages.Reservations_EndedCantChange);
            }

            var resourceChanged = request.ResourceId != reservation.ResourceId;
            var timeChanged = start != reservation.StartDateTime || end != reservation.EndDateTime;
            var startChanged = start != reservation.StartDateTime;
            var endChanged = end != reservation.EndDateTime;
            var reminderChanged = request.ReminderMinutes != reservation.ReminderMinutes;

            // Shortening it on the same resource claims nothing new: the reservation already
            // holds its whole old slot, so no other booking can be in the part it keeps. It's
            // also how someone ends early on a resource that has since gone into maintenance,
            // which the booking checks below would refuse.
            var staysWithinItsSlot = !resourceChanged
                && start >= reservation.StartDateTime && end <= reservation.EndDateTime;

            if (reservation.StartDateTime <= now)
            {
                // In progress: the start is history and the resource is in use.
                if (resourceChanged || start != reservation.StartDateTime)
                {
                    throw new ConflictException(Messages.Reservations_Started);
                }

                if (end <= now)
                {
                    throw new ConflictException(Messages.Reservations_EndInPast);
                }
            }
            else if (timeChanged && start < now - ReservationRules.PastStartGrace)
            {
                // Business rule 6, as on create - a 400 like the create validator's.
                throw new ValidationException(
                    [new ValidationFailure(nameof(UpdateReservationCommand.StartDateTime), Messages.Reservations_StartInPast)]);
            }
            else if (timeChanged && start > now + ReservationRules.MaxAdvance)
            {
                // The booking horizon, as on create (issue #41).
                throw new ValidationException(
                    [new ValidationFailure(
                        nameof(UpdateReservationCommand.StartDateTime),
                        string.Format(Messages.Reservations_TooFarAhead, ReservationRules.MaxAdvanceDays))]);
            }

            if (!staysWithinItsSlot)
            {
                // Moving the booking in time or to another resource is a new claim on that
                // resource, so it goes through the same checks as a new booking.
                await db.LockResourceForBookingAsync(request.ResourceId, cancellationToken);

                // Excluding itself: shifting 10:00-11:00 to 10:30-11:30 must not clash with its
                // own old slot.
                var resource = await BookingGuard.EnsureBookableAsync(
                    db, currentUser, request.ResourceId, start, end, reservation.Id, cancellationToken);

                reservation.ResourceId = request.ResourceId;
                reservation.StartDateTime = start;
                reservation.EndDateTime = end;

                // A new start or another resource is a new visit, claimed knowing what the door
                // asks for now (ADR-0030). Staying longer is the same visit and keeps what it was
                // booked with: a meeting that runs over in a room switched on since isn't
                // released for a check-in its owner never knew about.
                if (startChanged || resourceChanged)
                {
                    reservation.CheckInRequired = resource.CheckInRequired;
                }
            }
            else
            {
                reservation.StartDateTime = start;
                reservation.EndDateTime = end;
            }

            changed = resourceChanged || timeChanged || reminderChanged || note != reservation.Note;
            reservation.Note = note;

            // A new start or a new reminder time means the reminder is due at a new moment, so
            // it goes out again even if the old one was already sent (ADR-0018).
            if (startChanged || reminderChanged)
            {
                reservation.ReminderSentAt = null;
            }

            // Likewise a new end is asked about again before it comes (ADR-0033).
            if (endChanged)
            {
                reservation.EndingSoonSentAt = null;
            }

            // Likewise a check-in made for the old start or at the old resource's door is for a
            // visit that's no longer happening. Only a booking that hasn't started can be moved,
            // so this undoes at most a check-in from the few minutes before its start.
            if (startChanged || resourceChanged)
            {
                reservation.CheckedInAt = null;
            }

            reservation.ReminderMinutes = request.ReminderMinutes;

            // Who's invited now (ADR-0032), checked against the room the booking ends up in. The
            // organizer stays the owner, even when an Admin makes the change.
            var current = await db.ReservationAttendees
                .Where(a => a.ReservationId == reservation.Id)
                .Select(a => a.UserId)
                .ToListAsync(cancellationToken);
            var attendees = await invitations.CheckAsync(
                reservation.UserId, reservation.ResourceId, request.AttendeeIds, current, cancellationToken);
            if (attendees.Changed)
            {
                db.ReservationAttendees.RemoveRange(await db.ReservationAttendees
                    .Where(a => a.ReservationId == reservation.Id && attendees.Removed.Contains(a.UserId))
                    .ToListAsync(cancellationToken));
                db.ReservationAttendees.AddRange(attendees.Added
                    .Select(id => new ReservationAttendee { ReservationId = reservation.Id, UserId = id }));
                changed = true;
            }

            // A new start or another room is a new visit, as for the check-in: whoever is still
            // invited answers again.
            if (startChanged || resourceChanged)
            {
                var answered = await db.ReservationAttendees
                    .Where(a => a.ReservationId == reservation.Id
                        && a.Response != AttendeeResponse.Pending
                        && !attendees.Removed.Contains(a.UserId))
                    .ToListAsync(cancellationToken);
                foreach (var answer in answered)
                {
                    answer.Response = AttendeeResponse.Pending;
                    answer.RespondedAt = null;
                    answer.DeclineReason = null;
                }
            }

            // Newcomers are invited, people taken off are told, and the rest hear about a new
            // time or room. Written after every change above, so each message has the new details.
            if (attendees.ResourceName is { } resourceName)
            {
                messages.AddRange(invitations.Write(InboxMessageKind.MeetingInvitation, attendees.Added, reservation, resourceName, userId));
                messages.AddRange(invitations.Write(InboxMessageKind.MeetingUninvited, attendees.Removed, reservation, resourceName, userId));
                if (resourceChanged || timeChanged)
                {
                    messages.AddRange(invitations.Write(InboxMessageKind.MeetingChanged, attendees.Kept, reservation, resourceName, userId));
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var dto = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == request.Id)
            .Select(ReservationDto.Projection)
            .SingleAsync(cancellationToken);
        dto = await identityService.WithUserNameAsync(dto, cancellationToken);

        // A PUT that changes nothing (e.g. a retried save) doesn't broadcast.
        if (changed)
        {
            await notifier.ReservationUpdatedAsync(dto, cancellationToken);
        }

        await invitations.SendAsync(messages, cancellationToken);

        return dto;
    }
}
