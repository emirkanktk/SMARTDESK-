using FluentValidation;
using FluentValidation.Results;
using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Passkeys.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Passkeys.Commands.StartPasskeyCreation;

// The options the browser makes the caller a new passkey with (POST
// /api/auth/passkeys/creation-options, ADR-0057). Only with the account's password: otherwise an
// access token copied out of someone's browser would be enough to add a passkey of the copier's
// own, a way in that outlasts the session and even a password change.
public sealed record StartPasskeyCreationCommand(string Password) : IRequest<PasskeyOptionsDto>;

public sealed class StartPasskeyCreationCommandValidator : AbstractValidator<StartPasskeyCreationCommand>
{
    public StartPasskeyCreationCommandValidator()
    {
        RuleFor(x => x.Password).NotEmpty();
    }
}

public sealed class StartPasskeyCreationCommandHandler(
    ICurrentUserService currentUser, IIdentityService identityService, IPasskeys passkeys)
    : IRequestHandler<StartPasskeyCreationCommand, PasskeyOptionsDto>
{
    public async Task<PasskeyOptionsDto> Handle(StartPasskeyCreationCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        var user = await identityService.FindByIdAsync(userId, cancellationToken) ?? throw new UnauthorizedException();

        // Checked as a sign-in is, so wrong guesses here lock the account just the same.
        if (await identityService.CheckCredentialsAsync(user.Email, request.Password, cancellationToken) is null)
        {
            throw new ValidationException([new ValidationFailure(nameof(request.Password), Messages.Passkey_WrongPassword)]);
        }

        if ((await passkeys.ListAsync(userId, cancellationToken)).Count >= PasskeyRules.MaxPerUser)
        {
            throw PasskeyLimit.Reached();
        }

        return PasskeyOptionsDto.From(await passkeys.CreationOptionsAsync(userId, cancellationToken));
    }
}
