using MediatR;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Passkeys.Common;

namespace SmartDesk.Application.Features.Passkeys.Commands.StartPasskeySignIn;

// The options the browser asks for a passkey with on the sign-in page (POST
// /api/auth/passkeys/request-options, ADR-0057). Nobody is known yet, so they name no account:
// the device offers the passkeys it has for this site, and the person picks theirs.
public sealed record StartPasskeySignInCommand : IRequest<PasskeyOptionsDto>;

public sealed class StartPasskeySignInCommandHandler(IPasskeys passkeys)
    : IRequestHandler<StartPasskeySignInCommand, PasskeyOptionsDto>
{
    public async Task<PasskeyOptionsDto> Handle(StartPasskeySignInCommand request, CancellationToken cancellationToken) =>
        PasskeyOptionsDto.From(await passkeys.RequestOptionsAsync(cancellationToken));
}
