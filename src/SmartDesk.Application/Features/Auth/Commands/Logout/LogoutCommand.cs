using MediatR;

namespace SmartDesk.Application.Features.Auth.Commands.Logout;

// Sign-out: ends the session on the server, so its tokens stop working too (ADR-0026).
public sealed record LogoutCommand(string? RefreshToken) : IRequest;
