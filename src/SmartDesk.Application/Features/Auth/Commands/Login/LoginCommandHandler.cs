using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Application.Resources;

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

        return await SessionIssuer.SignInAsync(user, identityService, tokenGenerator, sessions, cancellationToken);
    }
}
