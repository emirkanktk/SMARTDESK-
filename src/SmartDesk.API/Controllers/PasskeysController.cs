using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartDesk.API.Authentication;
using SmartDesk.API.RateLimiting;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Features.Passkeys.Commands.AddPasskey;
using SmartDesk.Application.Features.Passkeys.Commands.PasskeySignIn;
using SmartDesk.Application.Features.Passkeys.Commands.RemovePasskey;
using SmartDesk.Application.Features.Passkeys.Commands.StartPasskeyCreation;
using SmartDesk.Application.Features.Passkeys.Commands.StartPasskeySignIn;
using SmartDesk.Application.Features.Passkeys.Common;
using SmartDesk.Application.Features.Passkeys.Queries.GetMyPasskeys;

namespace SmartDesk.API.Controllers;

// Passkeys (ADR-0057): Face ID, a fingerprint or Windows Hello instead of a password. Making one
// and signing in with one are two calls each: the options, with a token, and then the browser's
// answer with that token back. The page's address must be one the app is served at (403 if not),
// since a passkey belongs to its domain. Under /api/auth, so signing in with one sets the session
// cookie (ADR-0026) just as signing in with a password does.
[ApiController]
[Route("api/auth/passkeys")]
[Authorize]
public sealed class PasskeysController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PasskeyDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<PasskeyDto>> List(CancellationToken cancellationToken) =>
        sender.Send(new GetMyPasskeysQuery(), cancellationToken);

    // Takes the account's password, so it's limited as signing in is.
    [HttpPost("creation-options")]
    [EnableRateLimiting(RateLimitingSetup.LoginPolicy)]
    [ProducesResponseType<PasskeyOptionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<PasskeyOptionsDto> CreationOptions(StartPasskeyCreationCommand command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);

    [HttpPost]
    [ProducesResponseType<PasskeyDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Add(AddPasskeyCommand command, CancellationToken cancellationToken)
    {
        var passkey = await sender.Send(command, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, passkey);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Remove(string id, CancellationToken cancellationToken)
    {
        await sender.Send(new RemovePasskeyCommand(id), cancellationToken);
        return NoContent();
    }

    // The sign-in page's two calls count towards the same limit as signing in with a password.
    [HttpPost("request-options")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.LoginPolicy)]
    [ProducesResponseType<PasskeyOptionsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public Task<PasskeyOptionsDto> RequestOptions(CancellationToken cancellationToken) =>
        sender.Send(new StartPasskeySignInCommand(), cancellationToken);

    [HttpPost("sign-in")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.LoginPolicy)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<LoginResponse> SignIn(PasskeySignInCommand command, CancellationToken cancellationToken)
    {
        var signedIn = await sender.Send(command, cancellationToken);
        SessionCookie.Write(Response, signedIn);
        return signedIn.Response;
    }
}
