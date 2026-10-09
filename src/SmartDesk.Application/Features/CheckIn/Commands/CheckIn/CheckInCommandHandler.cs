using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.CheckIn.Commands.CheckIn;

public sealed class CheckInCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier,
    TimeProvider timeProvider,
    ICheckInTimes times) : IRequestHandler<CheckInCommand, ReservationDto>
{
    public async Task<ReservationDto> Handle(CheckInCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        // Another team's resource isn't there for the caller (ADR-0022).
        var resource = await db.Resources
            .AsNoTracking()
            .UsableBy(currentUser)
            .Where(r => r.Id == request.ResourceId)
            .Select(r => new { r.Name, r.CheckInRequired, r.CheckInCode, r.DoorScreenTokenHash })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Resource), request.ResourceId);

        if (!resource.CheckInRequired)
        {
            throw new ConflictException(string.Format(Messages.CheckIn_NotUsed, resource.Name))
            {
                Code = ConflictCodes.CheckInNotUsed,
            };
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // With a door screen, only its changing code checks in, so a photo of it or a shared
        // link stops working within a minute (ADR-0034). It's keyed by the screen's own key, not
        // the printed secret, which can't compute it (ADR-0039). Without a screen, the printed
        // code checks in, until it's replaced: then the page was opened from an old print or photo.
        if (resource.DoorScreenTokenHash is { } screenKey)
        {
            if (!CheckInRules.ChangingCodeMatches(screenKey, request.Code, now))
            {
                throw new ForbiddenAccessException(string.Format(Messages.CheckIn_CodeExpired, resource.Name));
            }
        }
        else if (!CheckInRules.CodeMatches(resource.CheckInCode, request.Code))
        {
            throw new ForbiddenAccessException(string.Format(Messages.CheckIn_WrongCode, resource.Name));
        }
        var nothingToCheckIn = new ConflictException(string.Format(
            Messages.CheckIn_NothingToCheckIn,
            resource.Name,
            (int)CheckInRules.OpensBefore.TotalMinutes,
            (int)times.Grace.TotalMinutes))
        {
            Code = ConflictCodes.NothingToCheckIn,
        };

        // Back-to-back bookings' windows can overlap for a few minutes: the one still waiting
        // for its check-in goes first, and one already checked in is simply answered again.
        var id = await OpenBookings(request.ResourceId, userId, now)
            .OrderBy(r => r.CheckedInAt == null ? 0 : 1)
            .ThenBy(r => r.StartDateTime)
            .Select(r => (Guid?)r.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw nothingToCheckIn;

        bool checkedInNow;

        // Under the reservation's own lock, like update, cancel and the release job: one moved,
        // cancelled or released meanwhile no longer passes the re-read.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockReservationAsync(id, cancellationToken);

            var reservation = await OpenBookings(request.ResourceId, userId, now)
                .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw nothingToCheckIn;

            // Idempotent: scanning twice checks in once and broadcasts once.
            checkedInNow = reservation.CheckedInAt is null;
            if (checkedInNow)
            {
                reservation.CheckedInAt = now;
                reservation.CheckedInByUserId = userId;
                await db.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }

        var dto = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ReservationDto.Projection)
            .SingleAsync(cancellationToken);
        dto = await identityService.WithUserNameAsync(dto, cancellationToken);

        if (checkedInNow)
        {
            await notifier.ReservationUpdatedAsync(dto, cancellationToken);
        }

        return dto;
    }

    // The Confirmed bookings of this resource the caller owns or is invited to (ADR-0032) that
    // need a check-in and are open for one at `now`. Whoever of the meeting gets to the door
    // first keeps the room; nobody else can.
    private IQueryable<Reservation> OpenBookings(Guid resourceId, Guid userId, DateTime now) =>
        db.Reservations
            .Where(r => r.ResourceId == resourceId && r.Status == ReservationStatus.Confirmed)
            .Where(r => r.UserId == userId || r.Attendees.Any(a => a.UserId == userId))
            .Where(CheckInRules.Needed)
            .Where(CheckInRules.OpenAt(now, times.Grace));
}
