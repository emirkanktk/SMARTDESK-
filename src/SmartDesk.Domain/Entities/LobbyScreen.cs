namespace SmartDesk.Domain.Entities;

// A screen in the office showing what's going on across it (ADR-0064): a TV in the lobby or by the
// kitchen, with nobody signed in on it. It proves itself with a token of its own, kept only as its
// hash, as a door screen does (ADR-0034).
public class LobbyScreen
{
    public Guid Id { get; set; }

    // What the Admins call it: "Entrance", "5th floor kitchen".
    public string Name { get; set; } = string.Empty;

    // The floor it shows, a resources' Location; null for the whole office.
    public string? Location { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    // When it last asked, to tell a screen that's on from one that's gone; noted once a minute at
    // most (LobbyScreenRules.SeenEvery).
    public DateTime? LastSeenAt { get; set; }
}
