using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartDesk.API.RateLimiting;
using SmartDesk.Application.Features.LobbyScreens.Queries.GetLobby;

namespace SmartDesk.API.Controllers;

// What a lobby screen shows (ADR-0064). The screen has no user: it proves itself with its own
// token, in a header so it stays out of addresses and logs, as a door screen does.
[ApiController]
[Route("api/lobby")]
[AllowAnonymous]
public sealed class LobbyController(ISender sender) : ControllerBase
{
    public const string TokenHeader = "X-Lobby-Token";

    [HttpGet]
    [EnableRateLimiting(RateLimitingSetup.LobbyScreenPolicy)]
    [ProducesResponseType<LobbyDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<LobbyDto> Get(
        [FromHeader(Name = TokenHeader)] string? token,
        [FromQuery] string? timeZone,
        CancellationToken cancellationToken) =>
        sender.Send(new GetLobbyQuery(token, timeZone), cancellationToken);
}
