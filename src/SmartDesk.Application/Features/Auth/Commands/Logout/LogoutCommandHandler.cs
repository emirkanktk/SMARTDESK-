using MediatR;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Auth.Commands.Logout;

public sealed class LogoutCommandHandler(IUserSessions sessions) : IRequestHandler<LogoutCommand>
{
    // Nothing to end without a refresh token; signing out twice is fine.
    public async Task Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            await sessions.EndAsync(request.RefreshToken, cancellationToken);
        }
    }
}
