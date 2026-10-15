using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Approvals.Common;

// Bookings that wait for an Admin's approval (ADR-0049): putting one to the Admins, and deciding
// it. A handler calls these while it holds the booking's locks, adds the messages they give to
// the same save, and sends them once that's committed (MeetingInvitations.SendAsync), so nobody
// is told about a change that rolled back.
public sealed class BookingApprovals(
    IApplicationDbContext db,
    IUserAccounts accounts,
    MeetingInvitations inbox,
    TimeProvider timeProvider)
{
    // `reservation` waits for an Admin from now on, with nothing decided yet, and every active
    // Admin is told who asked.
    public async Task<IReadOnlyList<InboxMessage>> RequestAsync(
        Reservation reservation, string resourceName, CancellationToken cancellationToken)
    {
        reservation.Status = ReservationStatus.Pending;
        reservation.RequestedAt = Now;
        reservation.DecidedAt = null;
        reservation.DecidedByUserId = null;
        reservation.DeclineReason = null;

        var admins = await accounts.ListAsync(new UserFilter(AccountStatus.Active, Roles.Admin), cancellationToken);
        return inbox.Write(
            InboxMessageKind.ApprovalRequested, admins.Select(a => a.Id), reservation, resourceName, reservation.UserId);
    }

    // No longer waiting: its kind doesn't ask any more. It's simply booked.
    public static void Book(Reservation reservation)
    {
        reservation.Status = ReservationStatus.Confirmed;
        reservation.RequestedAt = null;
    }

    // The booking an Admin decides, under its lock, with its resource: 404 when there's none,
    // 409 when it isn't waiting.
    public async Task<Reservation> FindWaitingAsync(Guid id, CancellationToken cancellationToken)
    {
        await db.LockReservationAsync(id, cancellationToken);
        var reservation = await db.Reservations
            .Include(r => r.Resource)
            .SingleOrDefaultAsync(r => r.Id == id, cancellationToken)
            ?? throw new NotFoundException(nameof(Reservation), id);
        if (reservation.Status != ReservationStatus.Pending)
        {
            throw new ConflictException(Messages.Approval_NotWaiting) { Code = ConflictCodes.NotAwaitingApproval };
        }

        return reservation;
    }

    // The booking as everyone's screens and its owner see it once a decision is committed.
    public async Task<ReservationDto> ReadAsync(Guid id, IIdentityService identityService, CancellationToken cancellationToken)
    {
        var dto = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == id)
            .Select(ReservationDto.Projection)
            .SingleAsync(cancellationToken);
        return await identityService.WithUserNameAsync(dto, cancellationToken);
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;
}
