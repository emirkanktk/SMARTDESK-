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
        // At the door, nobody needs to be signed in (ADR-0050): the code is what lets them in.
        var userId = currentUser.UserId;
        if (userId is null && !request.AtTheDoor)
        {
            throw new UnauthorizedException();
        }

        // Another team's resource isn't there for the caller (ADR-0022). At the door it is: the
        // code shows they're standing in front of it, whoever they are.
        var resources = request.AtTheDoor ? db.Resources : db.Resources.UsableBy(currentUser);
        var resource = await resources
            .AsNoTracking()
            .Where(r => r.Id == request.ResourceId)
            .Select(r => new { r.Name, r.CheckInRequired, r.CheckInCode, r.DoorScreenTokenHash })
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw new NotFoundException(nameof(Resource), request.ResourceId);

        var now = timeProvider.GetUtcNow().UtcDateTime;

        // At the door, a code that was never the resource's, or not lately, is the same 404 as no
        // resource: whoever knows only its id learns nothing, not even its name. One that opens
        // its page goes on, to be told why it can't check in.
        if (request.AtTheDoor
            && !CheckInRules.SeenAtTheDoor(resource.CheckInCode, resource.DoorScreenTokenHash, request.Code, now))
        {
            throw new NotFoundException(nameof(Resource), request.ResourceId);
        }

        if (!resource.CheckInRequired)
        {
            throw new ConflictException(string.Format(Messages.CheckIn_NotUsed, resource.Name))
            {
                Code = ConflictCodes.CheckInNotUsed,
            };
        }

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
            request.AtTheDoor ? Messages.CheckIn_NothingWaiting : Messages.CheckIn_NothingToCheckIn,
            resource.Name,
            (int)CheckInRules.OpensBefore.TotalMinutes,
            (int)times.Grace.TotalMinutes))
        {
            Code = ConflictCodes.NothingToCheckIn,
        };

        // Back-to-back bookings' windows can overlap for a few minutes: the one still waiting
        // for its check-in goes first, and one already checked in is simply answered again.
        var id = await OpenBookings(request, userId, now)
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

            var reservation = await OpenBookings(request, userId, now)
                .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
                ?? throw nothingToCheckIn;

            // Idempotent: scanning twice checks in once and broadcasts once.
            checkedInNow = reservation.CheckedInAt is null;
            if (checkedInNow)
            {
                reservation.CheckedInAt = now;
                // Nobody, when it was done at the door without an account.
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

    // The Confirmed bookings of this resource that need a check-in and are open for one at
    // `now`: the caller's own or those they're invited to (ADR-0032), so whoever of the meeting
    // gets to the door first keeps the room. At the door, anyone's (ADR-0050).
    private IQueryable<Reservation> OpenBookings(CheckInCommand request, Guid? userId, DateTime now)
    {
        var bookings = db.Reservations
            .Where(r => r.ResourceId == request.ResourceId && r.Status == ReservationStatus.Confirmed);
        if (!request.AtTheDoor)
        {
            bookings = bookings.Where(r => r.UserId == userId || r.Attendees.Any(a => a.UserId == userId));
        }

        return bookings
            .Where(CheckInRules.Needed)
            .Where(CheckInRules.OpenAt(now, times.Grace));
    }
}
