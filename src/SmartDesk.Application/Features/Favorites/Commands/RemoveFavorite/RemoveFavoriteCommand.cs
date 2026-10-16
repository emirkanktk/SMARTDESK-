using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Application.Features.Favorites.Commands.RemoveFavorite;

// Unstars a resource for the caller (DELETE /api/favorites/{resourceId}, ADR-0051): their own
// list, so even one that is now another team's. Nothing to do when it isn't starred.
public sealed record RemoveFavoriteCommand(Guid ResourceId) : IRequest;

public sealed class RemoveFavoriteCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    : IRequestHandler<RemoveFavoriteCommand>
{
    public async Task Handle(RemoveFavoriteCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        var favorite = await db.FavoriteResources
            .SingleOrDefaultAsync(f => f.UserId == userId && f.ResourceId == request.ResourceId, cancellationToken);
        if (favorite is not null)
        {
            db.FavoriteResources.Remove(favorite);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
