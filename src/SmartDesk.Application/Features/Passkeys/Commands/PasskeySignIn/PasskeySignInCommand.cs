using System.Text.Json;
using FluentValidation;
using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Application.Features.Passkeys.Common;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.Passkeys.Commands.PasskeySignIn;

// Signs in with a passkey (POST /api/auth/passkeys/sign-in, ADR-0057): Credential is what
// navigator.credentials.get() gave, Token the one that came with its options. The rest is as
// signing in with a password: the same account checks, and a session (ADR-0026).
public sealed record PasskeySignInCommand(JsonElement Credential, string Token) : IRequest<SignedIn>;

public sealed class PasskeySignInCommandValidator : AbstractValidator<PasskeySignInCommand>
{
    public PasskeySignInCommandValidator()
    {
        RuleFor(x => x.Credential).BrowserCredential();
        RuleFor(x => x.Token).ChallengeToken();
    }
}

public sealed class PasskeySignInCommandHandler(
    IPasskeys passkeys, IIdentityService identityService, IJwtTokenGenerator tokenGenerator, IUserSessions sessions)
    : IRequestHandler<PasskeySignInCommand, SignedIn>
{
    public async Task<SignedIn> Handle(PasskeySignInCommand request, CancellationToken cancellationToken)
    {
        var outcome = await passkeys.SignInAsync(request.Credential.GetRawText(), request.Token, cancellationToken);
        var user = outcome.Check switch
        {
            PasskeyCheck.Passed => outcome.User!,
            PasskeyCheck.Expired => throw new UnauthorizedException(Messages.Passkey_Expired),
            PasskeyCheck.Unknown => throw new UnauthorizedException(Messages.Passkey_Unknown),
            _ => throw new UnauthorizedException(Messages.Passkey_Failed),
        };

        return await SessionIssuer.SignInAsync(user, identityService, tokenGenerator, sessions, cancellationToken);
    }
}
