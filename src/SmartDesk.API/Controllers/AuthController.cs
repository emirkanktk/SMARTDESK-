using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartDesk.API.RateLimiting;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Features.Auth.Queries.GetCurrentUser;

namespace SmartDesk.API.Controllers;

[ApiController]
[Route("api/auth")]
[Authorize]
public sealed class AuthController(ISender sender) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.LoginPolicy)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<LoginResponse> Login(LoginCommand command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);

    [HttpGet("me")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<CurrentUserDto> Me(CancellationToken cancellationToken) =>
        sender.Send(new GetCurrentUserQuery(), cancellationToken);
}
