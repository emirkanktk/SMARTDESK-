using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.LobbyScreens.Common;

namespace SmartDesk.Application.Features.LobbyScreens.Queries.GetLobbyScreens;

// The office's lobby screens (GET /api/lobby-screens, Admin, ADR-0064), by name.
public sealed record GetLobbyScreensQuery : IRequest<IReadOnlyList<LobbyScreenDto>>;

public sealed class GetLobbyScreensQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetLobbyScreensQuery, IReadOnlyList<LobbyScreenDto>>
{
    public async Task<IReadOnlyList<LobbyScreenDto>> Handle(GetLobbyScreensQuery request, CancellationToken cancellationToken) =>
        await db.LobbyScreens
            .AsNoTracking()
            .OrderBy(s => s.Name)
            .ThenBy(s => s.CreatedAt)
            .Select(LobbyScreenDto.Projection)
            .ToListAsync(cancellationToken);
}
