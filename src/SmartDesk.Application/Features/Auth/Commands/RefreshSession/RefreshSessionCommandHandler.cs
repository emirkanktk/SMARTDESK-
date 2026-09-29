using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Auth.Commands.RefreshSession;

public sealed class RefreshSessionCommandHandler(
    IUserSessions sessions,
    IIdentityService identityService,
    IJwtTokenGenerator tokenGenerator) : IRequestHandler<RefreshSessionCommand, SignedIn>
{
    public async Task<SignedIn> Handle(RefreshSessionCommand request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.RefreshToken))
        {
            throw new UnauthorizedException(Messages.Auth_SessionEnded);
        }

        var renewal = await sessions.RenewAsync(request.RefreshToken, cancellationToken)
            ?? throw new UnauthorizedException(Messages.Auth_SessionEnded);

        // The account as it is now: switched off, or its password changed since the session
        // began, and the session is over.
        var user = await identityService.FindByIdAsync(renewal.UserId, cancellationToken);
        if (user is not { Status: AccountStatus.Active } || user.SessionStamp != renewal.SessionStamp)
        {
            await sessions.EndAsync(renewal.Session.RefreshToken, cancellationToken);
            throw new UnauthorizedException(Messages.Auth_SessionEnded);
        }

        return SessionIssuer.Answer(tokenGenerator, user, renewal.Session);
    }
}
