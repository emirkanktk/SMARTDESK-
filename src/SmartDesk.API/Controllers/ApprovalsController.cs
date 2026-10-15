using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.Approvals.Commands.ApproveReservation;
using SmartDesk.Application.Features.Approvals.Commands.DeclineReservation;
using SmartDesk.Application.Features.Approvals.Queries.GetApprovalRequests;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Constants;

namespace SmartDesk.API.Controllers;

// Bookings that wait for an Admin's approval (ADR-0049): the ones waiting, and the decision.
[ApiController]
[Route("api/approvals")]
[Authorize(Roles = Roles.Admin)]
public sealed class ApprovalsController(ISender sender) : ControllerBase
{
    // What waits for approval, the soonest first: each lapses at its start.
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReservationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<IReadOnlyList<ReservationDto>> Waiting(CancellationToken cancellationToken) =>
        sender.Send(new GetApprovalRequestsQuery(), cancellationToken);

    // Booked from now on, and whoever asked is told.
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ReservationDto> Approve(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new ApproveReservationCommand(id), cancellationToken);

    // Its time free again, and whoever asked is told why.
    [HttpPost("{id:guid}/decline")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ReservationDto> Decline(Guid id, DeclineReservationCommand command, CancellationToken cancellationToken) =>
        sender.Send(command with { Id = id }, cancellationToken);
}
