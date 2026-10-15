using System.Text.Json.Serialization;
using FluentValidation;
using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Approvals.Common;
using SmartDesk.Application.Features.Meetings.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Approvals.Commands.DeclineReservation;

// An Admin declines a booking that waits for approval (ADR-0049), saying why: its time is free
// again, whoever asked is told why, and anyone invited that it's off. 409 when it isn't waiting.
public sealed record DeclineReservationCommand(string Reason) : IRequest<ReservationDto>
{
    // Comes from the route (POST /api/approvals/{id}/decline), not the body.
    [JsonIgnore]
    public Guid Id { get; init; }
}

public sealed class DeclineReservationCommandValidator : AbstractValidator<DeclineReservationCommand>
{
    public const int ReasonMaxLength = 500;

    public DeclineReservationCommandValidator()
    {
        RuleFor(x => x.Reason)
            .Must(reason => !string.IsNullOrWhiteSpace(reason))
            .WithMessage(_ => Messages.Approval_ReasonRequired)
            .MaximumLength(ReasonMaxLength);
    }
}

public sealed class DeclineReservationCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IReservationNotifier notifier,
    MeetingInvitations inbox,
    BookingApprovals approvals,
    TimeProvider timeProvider) : IRequestHandler<DeclineReservationCommand, ReservationDto>
{
    public async Task<ReservationDto> Handle(DeclineReservationCommand request, CancellationToken cancellationToken)
    {
        var adminId = currentUser.UserId ?? throw new UnauthorizedException();
        var reason = request.Reason.Trim();
        List<InboxMessage> messages = [];

        await using (var transaction = await db.BeginTransactionAsync(cancellationToken))
        {
            var reservation = await approvals.FindWaitingAsync(request.Id, cancellationToken);

            reservation.Status = ReservationStatus.Cancelled;
            reservation.DecidedAt = timeProvider.GetUtcNow().UtcDateTime;
            reservation.DecidedByUserId = adminId;
            reservation.DeclineReason = reason;
            messages.AddRange(inbox.Write(
                InboxMessageKind.BookingDeclined, [reservation.UserId], reservation, reservation.Resource.Name, adminId, reason));
            messages.AddRange(await inbox.WriteCancellationAsync(reservation, adminId, cancellationToken));

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var dto = await approvals.ReadAsync(request.Id, identityService, cancellationToken);
        await notifier.ReservationCancelledAsync(dto, cancellationToken);
        await inbox.SendAsync(messages, cancellationToken);
        return dto;
    }
}
