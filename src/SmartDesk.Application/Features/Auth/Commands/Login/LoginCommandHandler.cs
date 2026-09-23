using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Auth.Commands.Login;

public sealed class LoginCommandHandler(IIdentityService identityService, IJwtTokenGenerator tokenGenerator)
    : IRequestHandler<LoginCommand, LoginResponse>
{
    public async Task<LoginResponse> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await identityService.CheckCredentialsAsync(request.Email, request.Password, cancellationToken)
            ?? throw new UnauthorizedException("Invalid email or password.");

        var token = tokenGenerator.Generate(user);

        return new LoginResponse(token.Token, token.ExpiresAtUtc);
    }
}
