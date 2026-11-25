namespace SmartDesk.Application.Common.Models;

// How a sign-in session began (ADR-0061).
public enum SignInMethod
{
    Password,
    Passkey,
}

// What a session starts with besides its user (ADR-0061): how they signed in, and the browser's
// own description of itself, its User-Agent header as it sent it.
public sealed record SessionOrigin(SignInMethod Method, string? UserAgent);

// One of the user's open sessions, in their list of signed-in devices (ADR-0061). UserAgent and
// Method are null for a session that began before devices were noted. LastActiveAt: its start or
// its latest renewal, which an open app makes about every quarter of an hour (ADR-0026).
// Current: the session the request that asked was made under.
public sealed record SignInSessionDto(
    Guid Id,
    string? UserAgent,
    SignInMethod? Method,
    DateTime StartedAt,
    DateTime LastActiveAt,
    bool Current);

// What "sign out everywhere else" did: how many sessions it ended.
public sealed record EndedSessionsDto(int Ended);
