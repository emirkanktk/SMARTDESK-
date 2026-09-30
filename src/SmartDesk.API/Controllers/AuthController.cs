using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SmartDesk.API.Authentication;
using SmartDesk.API.RateLimiting;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.Accounts.Commands.Register;
using SmartDesk.Application.Features.Auth.Commands.ChangePassword;
using SmartDesk.Application.Features.Auth.Commands.ForgotPassword;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Features.Auth.Commands.Logout;
using SmartDesk.Application.Features.Auth.Commands.RefreshSession;
using SmartDesk.Application.Features.Auth.Commands.ResetPassword;
using SmartDesk.Application.Features.Auth.Commands.UpdateProfile;
using SmartDesk.Application.Features.Auth.Queries.GetCurrentUser;
using SmartDesk.Application.Resources;

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
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<LoginResponse> Login(LoginCommand command, CancellationToken cancellationToken)
    {
        var signedIn = await sender.Send(command, cancellationToken);
        SessionCookie.Write(Response, signedIn);
        return signedIn.Response;
    }

    // A new access token from the session cookie (ADR-0026), which is replaced on the way. The
    // app calls it when it starts and shortly before its token expires. Without the cookie
    // there's nothing to renew: 204, so a guest's visit doesn't show up as a failed request.
    // A cookie whose session is over is a 401. Not rate limited: the refresh token has 256
    // random bits, and each tab renews every few minutes.
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<LoginResponse>> Refresh(CancellationToken cancellationToken)
    {
        var refreshToken = SessionCookie.Read(Request);
        if (string.IsNullOrEmpty(refreshToken))
        {
            return NoContent();
        }

        try
        {
            var signedIn = await sender.Send(new RefreshSessionCommand(refreshToken), cancellationToken);
            SessionCookie.Write(Response, signedIn);
            return signedIn.Response;
        }
        catch (UnauthorizedException ended)
        {
            // Answered here rather than by the exception handler, which would drop the header
            // that deletes the dead cookie.
            SessionCookie.Clear(Response);
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: Messages.Problem_Unauthorized, detail: ended.Message);
        }
    }

    // Ends the session on the server and deletes its cookie (ADR-0026). Anonymous, so signing
    // out still works once the access token has expired; doing it twice is fine.
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sender.Send(new LogoutCommand(SessionCookie.Read(Request)), cancellationToken);
        SessionCookie.Clear(Response);
        return NoContent();
    }

    // Signs up a Manager, who can sign in once an Admin approves (ADR-0020). Always 202 for
    // valid input, whether or not the email already has an account - its owner is emailed.
    [HttpPost("register")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.LoginPolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Register(RegisterCommand command, CancellationToken cancellationToken)
    {
        await sender.Send(command, cancellationToken);
        return Accepted();
    }

    // Always 202, whether or not an account has this email, so it can't be used to find out
    // which emails are registered (ADR-0019). The email goes out in the background.
    [HttpPost("forgot-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.LoginPolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        await sender.Send(command, cancellationToken);
        return Accepted();
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    [EnableRateLimiting(RateLimitingSetup.LoginPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        await sender.Send(command, cancellationToken);
        return NoContent();
    }

    // The user's own password change (ADR-0021): the only thing an account whose password was
    // set by someone else can do, besides /me. Every session ends, and this browser gets a new
    // one (ADR-0026).
    [HttpPost("change-password")]
    [AllowWhilePasswordChangeRequired]
    [EnableRateLimiting(RateLimitingSetup.LoginPolicy)]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<LoginResponse> ChangePassword(ChangePasswordCommand command, CancellationToken cancellationToken)
    {
        var signedIn = await sender.Send(command, cancellationToken);
        SessionCookie.Write(Response, signedIn);
        return signedIn.Response;
    }

    [HttpGet("me")]
    [AllowWhilePasswordChangeRequired]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<CurrentUserDto> Me(CancellationToken cancellationToken) =>
        sender.Send(new GetCurrentUserQuery(), cancellationToken);

    // The signed-in user's own name. The answer is the profile as it is now; the app then
    // renews its session, so its token carries the new name too.
    [HttpPut("me")]
    [ProducesResponseType<CurrentUserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<HttpValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<CurrentUserDto> UpdateProfile(UpdateProfileCommand command, CancellationToken cancellationToken) =>
        sender.Send(command, cancellationToken);
}
