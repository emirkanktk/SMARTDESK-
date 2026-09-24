using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.Reservations.Commands.CancelReservation;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Reservations.Queries.GetMyReservations;
using SmartDesk.Application.Features.Reservations.Queries.GetReservations;

namespace SmartDesk.API.Controllers;

[ApiController]
[Route("api/reservations")]
[Authorize]
public sealed class ReservationsController(ISender sender) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationDto>> Create(CreateReservationCommand command, CancellationToken cancellationToken)
    {
        var reservation = await sender.Send(command, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, reservation);
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ReservationDto> Cancel(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new CancelReservationCommand(id), cancellationToken);

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ReservationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<IReadOnlyList<ReservationDto>> GetInRange(
        [FromQuery] GetReservationsQuery query,
        CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);

    [HttpGet("mine")]
    [ProducesResponseType<IReadOnlyList<ReservationDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ReservationDto>> Mine(CancellationToken cancellationToken) =>
        sender.Send(new GetMyReservationsQuery(), cancellationToken);
}
