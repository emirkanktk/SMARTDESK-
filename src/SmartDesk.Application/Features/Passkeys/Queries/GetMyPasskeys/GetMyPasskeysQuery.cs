using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Passkeys.Queries.GetMyPasskeys;

// The caller's passkeys, newest first (GET /api/auth/passkeys, ADR-0057).
public sealed record GetMyPasskeysQuery : IRequest<IReadOnlyList<PasskeyDto>>;

public sealed class GetMyPasskeysQueryHandler(ICurrentUserService currentUser, IPasskeys passkeys)
    : IRequestHandler<GetMyPasskeysQuery, IReadOnlyList<PasskeyDto>>
{
    public Task<IReadOnlyList<PasskeyDto>> Handle(GetMyPasskeysQuery request, CancellationToken cancellationToken) =>
        passkeys.ListAsync(currentUser.UserId ?? throw new UnauthorizedException(), cancellationToken);
}
