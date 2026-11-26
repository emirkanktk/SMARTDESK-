using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.WhosIn.Queries.GetWhosIn;

namespace SmartDesk.API.Controllers;

[ApiController]
[Route("api/whos-in")]
[Authorize]
public sealed class WhosInController(ISender sender) : ControllerBase
{
    // Who's in the office on a day (ADR-0063): `date` (2026-11-26) is today when left out, and
    // `timeZone` (an IANA id) says where that day begins and ends, UTC when left out.
    [HttpGet]
    [ProducesResponseType<WhosInDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public Task<WhosInDto> Get([FromQuery] DateOnly? date, [FromQuery] string? timeZone, CancellationToken cancellationToken) =>
        sender.Send(new GetWhosInQuery(date, timeZone), cancellationToken);
}
