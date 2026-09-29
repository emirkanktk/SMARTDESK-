using MediatR;
using SmartDesk.Application.Features.Auth.Common;

namespace SmartDesk.Application.Features.Auth.Commands.RefreshSession;

// A new access token from the session's refresh token, which the API reads from its cookie
// (ADR-0026). The refresh token is replaced on the way.
public sealed record RefreshSessionCommand(string? RefreshToken) : IRequest<SignedIn>;
