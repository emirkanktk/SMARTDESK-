using System.Text.Json;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Features.Passkeys.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Passkeys.Commands.AddPasskey;

// Keeps the passkey the browser just made (POST /api/auth/passkeys, ADR-0057): Credential is what
// navigator.credentials.create() gave, Token the one that came with its options, and Name what the
// owner calls it in their list.
public sealed record AddPasskeyCommand(JsonElement Credential, string Token, string Name) : IRequest<PasskeyDto>;

public sealed class AddPasskeyCommandValidator : AbstractValidator<AddPasskeyCommand>
{
    public AddPasskeyCommandValidator()
    {
        RuleFor(x => x.Credential).BrowserCredential();
        RuleFor(x => x.Token).ChallengeToken();
        RuleFor(x => x.Name)
            .Cascade(CascadeMode.Stop)
            .Must(name => !string.IsNullOrWhiteSpace(name)).WithMessage(_ => Messages.Passkey_NameRequired)
            .MaximumLength(PasskeyRules.MaxNameLength)
            .Must(name => !name.Any(char.IsControl)).WithMessage(_ => Messages.Validation_NoControlCharacters);
    }
}

public sealed class AddPasskeyCommandHandler(ICurrentUserService currentUser, IPasskeys passkeys)
    : IRequestHandler<AddPasskeyCommand, PasskeyDto>
{
    public async Task<PasskeyDto> Handle(AddPasskeyCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        // Counted again: another tab may have added one since the options were given.
        if ((await passkeys.ListAsync(userId, cancellationToken)).Count >= PasskeyRules.MaxPerUser)
        {
            throw PasskeyLimit.Reached();
        }

        var added = await passkeys.AddAsync(
            userId, request.Credential.GetRawText(), request.Token, request.Name.Trim(), cancellationToken);
        return added.Check switch
        {
            PasskeyCheck.Passed => added.Passkey!,
            PasskeyCheck.Expired => throw Refused(Messages.Passkey_Expired),
            _ => throw Refused(Messages.Passkey_NotAdded),
        };
    }

    private static ValidationException Refused(string message) =>
        new([new ValidationFailure(nameof(AddPasskeyCommand.Credential), message)]);
}
