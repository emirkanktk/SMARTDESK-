using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SmartDesk.Application.Features.Favorites.Commands.AddFavorite;
using SmartDesk.Application.Features.Favorites.Commands.RemoveFavorite;
using SmartDesk.Application.Features.Favorites.Queries.GetFavorites;
using SmartDesk.Application.Features.Resources.Common;

namespace SmartDesk.API.Controllers;

// The signed-in user's favourite resources (ADR-0051): starred to find and book them quickly,
// the same on every device. Starring and unstarring are idempotent.
[ApiController]
[Route("api/favorites")]
[Authorize]
public sealed class FavoritesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ResourceDto>>(StatusCodes.Status200OK)]
    public Task<IReadOnlyList<ResourceDto>> Get(CancellationToken cancellationToken) =>
        sender.Send(new GetFavoritesQuery(), cancellationToken);

    [HttpPut("{resourceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Add(Guid resourceId, CancellationToken cancellationToken)
    {
        await sender.Send(new AddFavoriteCommand(resourceId), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{resourceId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Remove(Guid resourceId, CancellationToken cancellationToken)
    {
        await sender.Send(new RemoveFavoriteCommand(resourceId), cancellationToken);
        return NoContent();
    }
}
