using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.BookingRules.Commands.UpdateOfficeHours;
using SmartDesk.Application.Features.BookingRules.Commands.UpdateResourceTypeRules;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Features.BookingRules.Queries.GetBookingRules;
using SmartDesk.Domain.Constants;

namespace SmartDesk.API.Controllers;

// The rules bookings keep to (ADR-0046): anyone may read them, an Admin sets them.
[ApiController]
[Route("api/booking-rules")]
[Authorize]
public sealed class BookingRulesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<BookingRulesDto>(StatusCodes.Status200OK)]
    public Task<BookingRulesDto> Get(CancellationToken cancellationToken) =>
        sender.Send(new GetBookingRulesQuery(), cancellationToken);

    [HttpPut("office-hours")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<OfficeHoursDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<OfficeHoursDto> SetOfficeHours(UpdateOfficeHoursCommand command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);

    [HttpPut("types/{id:guid}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType<ResourceTypeRulesDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public Task<ResourceTypeRulesDto> SetTypeRules(Guid id, UpdateResourceTypeRulesCommand command, CancellationToken cancellationToken) =>
        sender.Send(command with { Id = id }, cancellationToken);
}
