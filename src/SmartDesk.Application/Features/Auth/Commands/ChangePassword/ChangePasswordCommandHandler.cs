using FluentValidation;
using FluentValidation.Results;
using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.Auth.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler(
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IJwtTokenGenerator tokenGenerator) : IRequestHandler<ChangePasswordCommand, LoginResponse>
{
    public static string WrongCurrentPasswordMessage => Messages.Auth_WrongCurrentPassword;

    public async Task<LoginResponse> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        var result = await identityService.ChangePasswordAsync(userId, request.CurrentPassword, request.NewPassword, cancellationToken);
        if (result.WrongCurrentPassword)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.CurrentPassword), WrongCurrentPasswordMessage)]);
        }

        if (!result.Succeeded)
        {
            throw new ValidationException(result.PasswordErrors.Select(error => new ValidationFailure(nameof(request.NewPassword), error)));
        }

        var token = tokenGenerator.Generate(result.User!);
        return new LoginResponse(token.Token, token.ExpiresAtUtc, PasswordChangeRequired: false);
    }
}
