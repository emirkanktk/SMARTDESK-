using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Sessions.Commands.EndOtherSessions;

// Signs out every device but this one (POST /api/auth/sessions/end-others, ADR-0061), the
// password staying as it is. Ending none is fine.
public sealed record EndOtherSessionsCommand : IRequest<EndedSessionsDto>;

public sealed class EndOtherSessionsCommandHandler(ICurrentUserService currentUser, IUserSessions sessions)
    : IRequestHandler<EndOtherSessionsCommand, EndedSessionsDto>
{
    public async Task<EndedSessionsDto> Handle(EndOtherSessionsCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        // Every token names its session (JwtBearerSetup refuses one that doesn't).
        var sessionId = currentUser.SessionId ?? throw new UnauthorizedException();
        return new EndedSessionsDto(await sessions.EndOthersAsync(userId, sessionId, cancellationToken));
    }
}
