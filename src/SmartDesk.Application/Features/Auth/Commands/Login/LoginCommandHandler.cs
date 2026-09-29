using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Auth.Commands.Login;

public sealed class LoginCommandHandler(IIdentityService identityService, IJwtTokenGenerator tokenGenerator)
    : IRequestHandler<LoginCommand, LoginResponse>
{
    public static string PendingApprovalMessage => Messages.Auth_PendingApproval;
    public static string DisabledMessage => Messages.Auth_Disabled;

    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
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

        var token = tokenGenerator.Generate(user);

        return new LoginResponse(token.Token, token.ExpiresAtUtc, user.MustChangePassword);
    }
}
