using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Sessions.Commands.EndSession;

// Signs one of the caller's devices out from here (DELETE /api/auth/sessions/{id}, ADR-0061): a
// lost phone, a shared computer. Its tokens stop working at once, and the app open there finds
// out and shows the sign-in page. Ending the caller's own session works too, as signing out.
public sealed record EndSessionCommand(Guid Id) : IRequest;

public sealed class EndSessionCommandHandler(ICurrentUserService currentUser, IUserSessions sessions)
    : IRequestHandler<EndSessionCommand>
{
    public async Task Handle(EndSessionCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();
        if (!await sessions.EndAsync(userId, request.Id, cancellationToken))
        {
            throw new NotFoundException("Session", request.Id);
        }
    }
}
