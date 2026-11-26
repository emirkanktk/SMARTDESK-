using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.LobbyScreens.Commands.DeleteLobbyScreen;

// Takes a lobby screen down (DELETE /api/lobby-screens/{id}, Admin, ADR-0064): its link stops
// working, and the screen says so on its next ask.
public sealed record DeleteLobbyScreenCommand(Guid Id) : IRequest;

public sealed class DeleteLobbyScreenCommandHandler(IApplicationDbContext db) : IRequestHandler<DeleteLobbyScreenCommand>
{
    public async Task Handle(DeleteLobbyScreenCommand request, CancellationToken cancellationToken)
    {
        var screen = await db.LobbyScreens.SingleOrDefaultAsync(s => s.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(LobbyScreen), request.Id);
        db.LobbyScreens.Remove(screen);
        await db.SaveChangesAsync(cancellationToken);
    }
}
