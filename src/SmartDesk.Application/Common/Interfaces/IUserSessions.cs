using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Common.Interfaces;

// Sign-in sessions (ADR-0026). A session outlives the short access tokens issued under it: the
// browser renews them with a refresh token it keeps in an HttpOnly cookie, and each renewal
// replaces that refresh token. A session ends on sign-out, after too long without use, or at its
// hard limit; a refresh token that was already replaced and is used again ends it at once, since
// only a copy could still be using it. Its owner sees their open ones and can end any of them
// from another device (ADR-0061).
public interface IUserSessions
{
    // A new session for the user, bound to their current security stamp, noting how it began.
    Task<IssuedSession> StartAsync(Guid userId, string sessionStamp, SessionOrigin origin, CancellationToken cancellationToken);

    // Replaces the refresh token and extends the session. Null when the token is unknown, the
    // session has ended or expired, or the token was already replaced (which ends the session).
    // A renewal that another with the same token beat by a moment keeps the session's new
    // token as it is: its Session.RefreshToken is null.
    Task<SessionRenewal?> RenewAsync(string refreshToken, CancellationToken cancellationToken);

    // Sign-out: ends the session this refresh token belongs to, if any.
    Task EndAsync(string refreshToken, CancellationToken cancellationToken);

    // Ends every session of the user, e.g. after a password change.
    Task EndAllAsync(Guid userId, CancellationToken cancellationToken);

    // The user's sessions that can still be renewed, the one used most recently first. Current
    // marks currentSessionId's.
    Task<IReadOnlyList<SignInSessionDto>> ListOpenAsync(Guid userId, Guid? currentSessionId, CancellationToken cancellationToken);

    // Ends one of the user's sessions by its id, from whichever device: its tokens stop working at
    // once. False when the user has no such session still open.
    Task<bool> EndAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken);

    // Ends every session of the user but keepSessionId's: "sign out everywhere else". Returns how
    // many it ended.
    Task<int> EndOthersAsync(Guid userId, Guid keepSessionId, CancellationToken cancellationToken);
}

// RefreshToken is shown to the client once and stored only as a hash; null when the client
// keeps the one it has (see RenewAsync).
public sealed record IssuedSession(Guid SessionId, string? RefreshToken, DateTime ExpiresAt);

// SessionStamp: the user's stamp when the session started, to compare with the current one.
public sealed record SessionRenewal(Guid UserId, string SessionStamp, IssuedSession Session);
