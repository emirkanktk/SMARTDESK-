using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Passkeys.Commands.RemovePasskey;

// Takes one of the caller's passkeys off their account (DELETE /api/auth/passkeys/{id}, ADR-0057):
// it signs nobody in from then on, though the device may offer it until it's deleted there too.
// Sessions it started go on, as those a password started do.
public sealed record RemovePasskeyCommand(string Id) : IRequest;

public sealed class RemovePasskeyCommandHandler(ICurrentUserService currentUser, IPasskeys passkeys)
    : IRequestHandler<RemovePasskeyCommand>
{
    public async Task Handle(RemovePasskeyCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        if (!await passkeys.RemoveAsync(userId, request.Id, cancellationToken))
        {
            throw new NotFoundException("Passkey", request.Id);
        }
    }
}
