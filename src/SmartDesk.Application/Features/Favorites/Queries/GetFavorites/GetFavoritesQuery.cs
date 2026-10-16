using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.Application.Features.Favorites.Queries.GetFavorites;

// The caller's favourite resources (GET /api/favorites, ADR-0051), by name, as the resource list
// shows them. Only those they may still use (ADR-0022): one that moved to another team stays
// starred but out of sight until it's theirs again. Whatever their status, so a favourite in
// maintenance says so rather than going missing.
public sealed record GetFavoritesQuery : IRequest<IReadOnlyList<ResourceDto>>;

public sealed class GetFavoritesQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser, ResourceReader reader)
    : IRequestHandler<GetFavoritesQuery, IReadOnlyList<ResourceDto>>
{
    public async Task<IReadOnlyList<ResourceDto>> Handle(GetFavoritesQuery request, CancellationToken cancellationToken)
    {
        var userId = currentUser.UserId ?? throw new UnauthorizedException();

        var favorites = db.Resources
            .UsableBy(currentUser)
            .Where(r => db.FavoriteResources.Any(f => f.UserId == userId && f.ResourceId == r.Id))
            .OrderBy(r => r.Name);
        return await reader.ListAsync(favorites, cancellationToken);
    }
}
