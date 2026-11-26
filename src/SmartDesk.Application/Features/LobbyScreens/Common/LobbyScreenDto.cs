using System.Linq.Expressions;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.LobbyScreens.Common;

// A lobby screen as its Admins see it (ADR-0064): what it's called, the floor it shows (null for
// the whole office), and when it last asked, so a screen that's off stands out.
public sealed record LobbyScreenDto(Guid Id, string Name, string? Location, DateTime CreatedAt, DateTime? LastSeenAt)
{
    public static readonly Expression<Func<LobbyScreen, LobbyScreenDto>> Projection =
        s => new LobbyScreenDto(s.Id, s.Name, s.Location, s.CreatedAt, s.LastSeenAt);

    // For a screen just written, compiled once (after Projection, which it needs).
    private static readonly Func<LobbyScreen, LobbyScreenDto> Map = Projection.Compile();

    public static LobbyScreenDto From(LobbyScreen screen) => Map(screen);
}

// A screen just set up, with the link to open on it: shown this once, since only its token's hash
// is kept.
public sealed record NewLobbyScreenDto(LobbyScreenDto Screen, string Link);
