using FluentValidation.Results;
using FluentValidation;
using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.Auth.Commands.ChangePassword;

public sealed class ChangePasswordCommandHandler(
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IJwtTokenGenerator tokenGenerator,
    IUserSessions sessions) : IRequestHandler<ChangePasswordCommand, SignedIn>
{
    public static string WrongCurrentPasswordMessage => Messages.Auth_WrongCurrentPassword;

    public async Task<SignedIn> Handle(ChangePasswordCommand request, CancellationToken cancellationToken)
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

        // Every session ends, this one included; the answer starts a new one here (ADR-0026).
        await sessions.EndAllAsync(userId, cancellationToken);
        var session = await sessions.StartAsync(userId, result.User!.SessionStamp, cancellationToken);
        return SessionIssuer.Answer(tokenGenerator, result.User, session);
    }
}
