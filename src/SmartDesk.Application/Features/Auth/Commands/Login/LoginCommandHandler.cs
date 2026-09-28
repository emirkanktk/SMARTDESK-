using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Auth.Commands.Login;

public sealed class LoginCommandHandler(IIdentityService identityService, IJwtTokenGenerator tokenGenerator)
    : IRequestHandler<LoginCommand, LoginResponse>
{
    public const string PendingApprovalMessage = "Your account is waiting for an administrator's approval. We'll email you when it's ready.";
    public const string DisabledMessage = "Your account has been disabled. Contact your administrator.";

    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await identityService.CheckCredentialsAsync(request.Email, request.Password, cancellationToken)
            ?? throw new UnauthorizedException("Invalid email or password.");

        // Only after the password matched, so these say nothing to someone guessing emails.
        switch (user.Status)
        {
            case AccountStatus.PendingApproval:
                throw new ForbiddenAccessException(PendingApprovalMessage);
            case AccountStatus.Disabled:
                throw new ForbiddenAccessException(DisabledMessage);
        }

        var token = tokenGenerator.Generate(user);

        return new LoginResponse(token.Token, token.ExpiresAtUtc);
    }
}
