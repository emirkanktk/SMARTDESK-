using FluentValidation;
using FluentValidation.Results;
using MediatR;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.Auth.Commands.ResetPassword;

public sealed class ResetPasswordCommandHandler(IIdentityService identityService)
    : IRequestHandler<ResetPasswordCommand>
{
    public static string InvalidLinkMessage => Messages.Auth_InvalidResetLink;

    public async Task Handle(ResetPasswordCommand request, CancellationToken cancellationToken)
    {
        var result = await identityService.ResetPasswordAsync(
            request.Email.Trim(), request.Token, request.NewPassword, cancellationToken);

        if (result.TokenInvalid)
        {
            throw new ValidationException([new ValidationFailure(nameof(ResetPasswordCommand.Token), InvalidLinkMessage)]);
        }

        if (!result.Succeeded)
        {
            throw new ValidationException(result.PasswordErrors
                .Select(error => new ValidationFailure(nameof(ResetPasswordCommand.NewPassword), error)));
        }
    }
}
