using SmartDesk.Application.Features.Auth.Commands.Login;

namespace SmartDesk.Application.Features.Auth.Common;

// What signing in, renewing or changing the password produces (ADR-0026): the response body,
// and the session's refresh token, which the API puts in an HttpOnly cookie and never in the body.
// No refresh token: the cookie the browser has stays as it is.
public sealed record SignedIn(LoginResponse Response, string? RefreshToken, DateTime RefreshExpiresAt)
{
    // The token the browser is known by from now on (ADR-0062), for a cookie of its own that
    // outlives the session. Only signing in sets it; null leaves that cookie as it is.
    public string? DeviceToken { get; init; }
}
