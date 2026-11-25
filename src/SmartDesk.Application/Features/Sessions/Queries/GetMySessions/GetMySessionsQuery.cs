using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Sessions.Queries.GetMySessions;

// The devices the caller is signed in on (GET /api/auth/sessions, ADR-0061): this one first,
// then the others, most recently used first.
public sealed record GetMySessionsQuery : IRequest<IReadOnlyList<SignInSessionDto>>;

public sealed class GetMySessionsQueryHandler(ICurrentUserService currentUser, IUserSessions sessions)
    : IRequestHandler<GetMySessionsQuery, IReadOnlyList<SignInSessionDto>>
{
    public Task<IReadOnlyList<SignInSessionDto>> Handle(GetMySessionsQuery request, CancellationToken cancellationToken) =>
        sessions.ListOpenAsync(currentUser.UserId ?? throw new UnauthorizedException(), currentUser.SessionId, cancellationToken);
}
