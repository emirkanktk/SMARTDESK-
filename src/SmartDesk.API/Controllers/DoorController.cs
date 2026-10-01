using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartDesk.API.RateLimiting;
using SmartDesk.Application.Features.CheckIn.Queries.GetDoorScreen;

namespace SmartDesk.API.Controllers;

// For the screen at a resource's door (ADR-0034), which has no user: it proves itself with its
// own token, in a header, so the token stays out of addresses and logs.
[ApiController]
[Route("api/door")]
[AllowAnonymous]
public sealed class DoorController(ISender sender) : ControllerBase
{
    public const string TokenHeader = "X-Door-Token";

    [HttpGet("{resourceId:guid}")]
    [EnableRateLimiting(RateLimitingSetup.DoorScreenPolicy)]
    [ProducesResponseType<DoorScreenDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<DoorScreenDto> Get(
        Guid resourceId,
        [FromHeader(Name = TokenHeader)] string? token,
        [FromQuery] string? timeZone,
        CancellationToken cancellationToken) =>
        sender.Send(new GetDoorScreenQuery(resourceId, token, timeZone), cancellationToken);
}
