using SmartDesk.Application.Features.Auth.Commands.Login;

namespace SmartDesk.Application.Features.Auth.Common;

// What signing in, renewing or changing the password produces (ADR-0026): the response body,
// and the session's refresh token, which the API puts in an HttpOnly cookie and never in the body.
public sealed record SignedIn(LoginResponse Response, string RefreshToken, DateTime RefreshExpiresAt);
