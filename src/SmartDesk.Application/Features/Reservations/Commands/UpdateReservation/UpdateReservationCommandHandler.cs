using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;

public sealed class UpdateReservationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier,
    TimeProvider timeProvider) : IRequestHandler<UpdateReservationCommand, ReservationDto>
{
    public async Task<ReservationDto> Handle(UpdateReservationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var start = request.StartDateTime.AsUtc();
        var end = request.EndDateTime.AsUtc();
        var note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim();
        bool changed;

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
                throw new ForbiddenAccessException("You can only change your own reservations.");
            }

            if (reservation.Status == ReservationStatus.Cancelled)
            {
                throw new ConflictException("A cancelled reservation can't be changed.");
            }

            var now = timeProvider.GetUtcNow().UtcDateTime;
            if (reservation.EndDateTime <= now)
            {
                throw new ConflictException("This reservation has already ended and can't be changed.");
            }

            var resourceChanged = request.ResourceId != reservation.ResourceId;
            var timeChanged = start != reservation.StartDateTime || end != reservation.EndDateTime;
            var startChanged = start != reservation.StartDateTime;
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
                    throw new ConflictException(
                        "This reservation has already started; only its end time and note can be changed.");
                }

                if (end <= now)
                {
                    throw new ConflictException("The end time can't be moved into the past.");
                }
            }
            else if (timeChanged && start < now - ReservationRules.PastStartGrace)
            {
                // Business rule 6, as on create - a 400 like the create validator's.
                throw new ValidationException(
                    [new ValidationFailure(nameof(UpdateReservationCommand.StartDateTime), "A reservation can't start in the past.")]);
            }

            if (!staysWithinItsSlot)
            {
                // Moving the booking in time or to another resource is a new claim on that
                // resource, so it goes through the same checks as a new booking.
                await db.LockResourceForBookingAsync(request.ResourceId, cancellationToken);

                // Excluding itself: shifting 10:00-11:00 to 10:30-11:30 must not clash with its
                // own old slot.
                await BookingGuard.EnsureBookableAsync(
                    db, currentUser, request.ResourceId, start, end, reservation.Id, cancellationToken);

                reservation.ResourceId = request.ResourceId;
                reservation.StartDateTime = start;
                reservation.EndDateTime = end;
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

            reservation.ReminderMinutes = request.ReminderMinutes;

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

        return dto;
    }
}
