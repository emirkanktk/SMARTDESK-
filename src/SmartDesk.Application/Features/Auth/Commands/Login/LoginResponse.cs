namespace SmartDesk.Application.Features.Auth.Commands.Login;

// PasswordChangeRequired: the password was set by someone else; until the user chooses a new one
// (POST /api/auth/change-password) every other endpoint answers 403 (ADR-0021).
public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt, bool PasswordChangeRequired = false);
