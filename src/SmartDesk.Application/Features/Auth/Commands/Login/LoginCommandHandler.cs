using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Auth.Commands.Login;

public sealed class LoginCommandHandler(IIdentityService identityService, IJwtTokenGenerator tokenGenerator, IUserSessions sessions)
    : IRequestHandler<LoginCommand, SignedIn>
{
    public static string PendingApprovalMessage => Messages.Auth_PendingApproval;
    public static string DisabledMessage => Messages.Auth_Disabled;

    public async Task<SignedIn> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await identityService.CheckCredentialsAsync(request.Email, request.Password, cancellationToken)
            ?? throw new UnauthorizedException(Messages.Auth_InvalidCredentials);

        // Only after the password matched, so these say nothing to someone guessing emails.
        switch (user.Status)
        {
            case AccountStatus.PendingApproval:
                throw new ForbiddenAccessException(PendingApprovalMessage);
            case AccountStatus.Disabled:
                throw new ForbiddenAccessException(DisabledMessage);
        }

        if (user.Language != Languages.Current)
        {
            await identityService.RememberLanguageAsync(user.Id, Languages.Current, cancellationToken);
        }

        var session = await sessions.StartAsync(user.Id, user.SessionStamp, cancellationToken);
        return SessionIssuer.Answer(tokenGenerator, user, session);
    }
}
