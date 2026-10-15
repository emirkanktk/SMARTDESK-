using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Approvals.Common;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Commands.CreateReservation;

public sealed class CreateReservationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier,
    MeetingInvitations invitations,
    BookingPolicy policy,
    BookingApprovals approvals) : IRequestHandler<CreateReservationCommand, ReservationDto>
{
    public async Task<ReservationDto> Handle(CreateReservationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var start = request.StartDateTime.AsUtc();
        var end = request.EndDateTime.AsUtc();
        var reservationId = Guid.CreateVersion7();
        IReadOnlyList<InboxMessage> messages;

        // Concurrency (business rule 5, ADR-0011): bookings of the same resource are serialized
        // by a lock held until commit, so the checks below always see the latest committed
        // reservations. The database's exclusion constraint stays as the last line of defence;
        // if it ever fires, Infrastructure reports it as the same ConflictException.
        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            await db.LockResourceForBookingAsync(request.ResourceId, cancellationToken);

            // Exists for the caller and Active (business rule 3); within its kind's rules, and
            // not one more than its owner may have (ADR-0046); not blocked, no overlap (business
            // rules 1 and 7).
            var resource = await BookingGuard.EnsureResourceBookableAsync(db, currentUser, request.ResourceId, cancellationToken);
            await policy.EnsureAllowedAsync(new BookingClaim(userId, request.ResourceId, start, end), cancellationToken);

            // A kind that waits for an Admin's approval (ADR-0049): asked for, not booked yet.
            var needsApproval = await policy.NeedsApprovalAsync(
                request.ResourceId, start, currentUser.IsInRole(Roles.Admin), cancellationToken);
            await BookingGuard.EnsureFreeAsync(db, request.ResourceId, resource.Name, start, end, null, cancellationToken);

            // Who may come, and whether they fit (ADR-0032).
            var attendees = await invitations.CheckAsync(userId, request.ResourceId, request.AttendeeIds, [], cancellationToken);

            var reservation = new Reservation
            {
                Id = reservationId,
                ResourceId = request.ResourceId,
                UserId = userId,
                StartDateTime = start,
                EndDateTime = end,
                Status = ReservationStatus.Confirmed,
                Note = string.IsNullOrWhiteSpace(request.Note) ? null : request.Note.Trim(),
                ReminderMinutes = request.ReminderMinutes,
                // Booked knowing the door asks for a check-in, or not (ADR-0030).
                CheckInRequired = resource.CheckInRequired,
                Attendees = attendees.Attendees.Select(id => new ReservationAttendee { UserId = id }).ToList(),
            };
            db.Reservations.Add(reservation);
            messages = invitations.Write(InboxMessageKind.MeetingInvitation, attendees.Added, reservation, resource.Name, userId);
            if (needsApproval)
            {
                messages = [.. messages, .. await approvals.RequestAsync(reservation, resource.Name, cancellationToken)];
            }

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        // Only after commit: never announce a reservation that could still roll back.
        var dto = await db.Reservations
            .AsNoTracking()
            .Where(r => r.Id == reservationId)
            .Select(ReservationDto.Projection)
            .SingleAsync(cancellationToken);
        dto = await identityService.WithUserNameAsync(dto, cancellationToken);

        await notifier.ReservationCreatedAsync(dto, cancellationToken);
        await invitations.SendAsync(messages, cancellationToken);

        return dto;
    }
}
