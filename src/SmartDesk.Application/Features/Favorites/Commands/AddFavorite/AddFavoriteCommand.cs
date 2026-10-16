using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Favorites.Commands.AddFavorite;

// Stars a resource for the caller (PUT /api/favorites/{resourceId}, ADR-0051): one they may use
// (ADR-0022), whatever its status. Starring it again changes nothing.
public sealed record AddFavoriteCommand(Guid ResourceId) : IRequest;

public sealed class AddFavoriteCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser, TimeProvider timeProvider)
    : IRequestHandler<AddFavoriteCommand>
{
    public async Task Handle(AddFavoriteCommand request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        // Another team's resource isn't there for the caller.
        if (!await db.Resources.UsableBy(currentUser).AnyAsync(r => r.Id == request.ResourceId, cancellationToken))
        {
            throw new NotFoundException(nameof(Resource), request.ResourceId);
        }

        var mine = db.FavoriteResources.Where(f => f.UserId == userId);
        if (await mine.AnyAsync(f => f.ResourceId == request.ResourceId, cancellationToken))
        {
            return;
        }

        // Counted without a lock: two stars at the same moment may make one more than the most,
        // which is no harm.
        if (await mine.CountAsync(cancellationToken) >= FavoriteRules.MaxPerUser)
        {
            throw new ConflictException(string.Format(Messages.Favorites_Full, FavoriteRules.MaxPerUser))
            {
                Code = ConflictCodes.FavoritesFull,
            };
        }

        db.FavoriteResources.Add(new FavoriteResource
        {
            UserId = userId,
            ResourceId = request.ResourceId,
            CreatedAt = timeProvider.GetUtcNow().UtcDateTime,
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Starred from another tab meanwhile: it's a favourite either way.
            if (!await StarredAsync(userId, request.ResourceId, cancellationToken))
            {
                throw;
            }
        }
    }

    private Task<bool> StarredAsync(Guid userId, Guid resourceId, CancellationToken cancellationToken) =>
        db.FavoriteResources.AsNoTracking().AnyAsync(f => f.UserId == userId && f.ResourceId == resourceId, cancellationToken);
}
