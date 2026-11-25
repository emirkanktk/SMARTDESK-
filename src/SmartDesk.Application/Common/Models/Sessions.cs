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

// Whether an account knew the browser it just signed in on (ADR-0062).
public enum DeviceFamiliarity
{
    // It has signed in on it before.
    Known,

    // It hasn't, but it has on others: its owner is told.
    New,

    // It knows no browser at all - a new account, or one that hasn't signed in since browsers
    // were noted - so there's nothing to tell this one from, and nobody is told.
    First,
}

// Token: the browser's own, which its cookie keeps.
public sealed record DeviceCheck(DeviceFamiliarity Familiarity, string Token);

// A sign-in on a browser its account didn't know (ADR-0062), as its owner is told of it: on what,
// when, and how. Session: the one it started, which they can end from their list of devices.
public sealed record NewSignIn(Guid Session, string? UserAgent, SignInMethod Method, DateTime At);
