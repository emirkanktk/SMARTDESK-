using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.Meetings.Commands.RespondToInvitation;
using SmartDesk.Application.Features.Meetings.Queries.GetInvitees;
using SmartDesk.Application.Features.Meetings.Queries.GetMyInvitations;
using SmartDesk.Application.Features.Reservations.Commands.CancelFollowing;
using SmartDesk.Application.Features.Reservations.Commands.CancelReservation;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservationSeries;
using SmartDesk.Application.Features.Reservations.Commands.EndReservation;
using SmartDesk.Application.Features.Reservations.Commands.ExtendReservation;
using SmartDesk.Application.Features.Reservations.Commands.UpdateReservation;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Reservations.Queries.GetExtensionOptions;
using SmartDesk.Application.Features.Reservations.Queries.GetMyReservations;
using SmartDesk.Application.Features.Reservations.Queries.GetReservationById;
using SmartDesk.Application.Features.Reservations.Queries.GetReservations;
using SmartDesk.Application.Features.Reservations.Queries.GetSeriesPreview;

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

    // A booking that repeats (ADR-0035): every date booked at once, or - with skipConflicts -
    // the free ones.
    [HttpPost("series")]
    [ProducesResponseType<ReservationSeriesResultDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationSeriesResultDto>> CreateSeries(
        CreateReservationSeriesCommand command, CancellationToken cancellationToken)
    {
        var result = await sender.Send(command, cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    // The dates a repeating booking would have, and which are free (ADR-0035).
    [HttpGet("series/preview")]
    [ProducesResponseType<SeriesPreviewDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<SeriesPreviewDto> PreviewSeries([FromQuery] GetSeriesPreviewQuery query, CancellationToken cancellationToken) =>
        sender.Send(query, cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ReservationDto> GetById(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetReservationByIdQuery(id), cancellationToken);

    [HttpPut("{id:guid}")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ReservationDto> Update(Guid id, UpdateReservationCommand command, CancellationToken cancellationToken) =>
        sender.Send(command with { Id = id }, cancellationToken);

    // Ends a booking under way now, so the room is free at once (ADR-0034); owner or Admin.
    [HttpPost("{id:guid}/end")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ReservationDto> End(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new EndReservationCommand(id), cancellationToken);

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ReservationDto> Cancel(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new CancelReservationCommand(id), cancellationToken);

    // A date of a repeating booking and every later one still to come (ADR-0035): the dates
    // cancelled now.
    [HttpPost("{id:guid}/cancel-following")]
    [ProducesResponseType<IReadOnlyList<ReservationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<IReadOnlyList<ReservationDto>> CancelFollowing(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new CancelFollowingCommand(id), cancellationToken);

    // How much longer the booking can run (ADR-0033): the extension options that still fit.
    [HttpGet("{id:guid}/extension")]
    [ProducesResponseType<ExtensionOptionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ExtensionOptionsDto> ExtensionOptions(Guid id, CancellationToken cancellationToken) =>
        sender.Send(new GetExtensionOptionsQuery(id), cancellationToken);

    // Lets the booking run longer by 15, 30 or 60 minutes (ADR-0033), for the owner or an Admin.
    [HttpPost("{id:guid}/extend")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ReservationDto> Extend(Guid id, ExtendReservationCommand command, CancellationToken cancellationToken) =>
        sender.Send(command with { Id = id }, cancellationToken);

    // The caller's answer to a meeting they're invited to (ADR-0032): Accepted, or Declined with
    // a reason for the organizer.
    [HttpPost("{id:guid}/response")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public Task<ReservationDto> Respond(Guid id, RespondToInvitationCommand command, CancellationToken cancellationToken) =>
        sender.Send(command with { Id = id }, cancellationToken);

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

    // Other people's meetings the caller is invited to and that aren't over (ADR-0032).
    [HttpGet("invited")]
    [ProducesResponseType<IReadOnlyList<ReservationDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ReservationDto>> Invited(CancellationToken cancellationToken) =>
        sender.Send(new GetMyInvitationsQuery(), cancellationToken);

    // Who the caller may put on a booking's list: a Manager's team, everyone for an Admin, nobody
    // for an Employee.
    [HttpGet("invitees")]
    [ProducesResponseType<IReadOnlyList<InviteeDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<InviteeDto>> Invitees(CancellationToken cancellationToken) =>
        sender.Send(new GetInviteesQuery(), cancellationToken);
}
