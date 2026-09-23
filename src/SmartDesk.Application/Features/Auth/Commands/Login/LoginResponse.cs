namespace SmartDesk.Application.Features.Auth.Commands.Login;

public sealed record LoginResponse(string AccessToken, DateTime ExpiresAt);
