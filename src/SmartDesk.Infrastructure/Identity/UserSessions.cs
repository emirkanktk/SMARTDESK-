using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Infrastructure.Persistence;

namespace SmartDesk.Infrastructure.Identity;

// Sessions in the database (ADR-0026). Every change ends in UserAccessCache.Forget, so a
// request under a session that just ended is refused at once rather than when the cache expires.
public sealed class UserSessions(
    SmartDeskDbContext db,
    UserAccessCache accessCache,
    IOptions<SessionOptions> options,
    TimeProvider timeProvider) : IUserSessions
{
    // 256 random bits: guessing one is not an option, so no rate limit is needed on renewal.
    private const int TokenBytes = 32;

    public async Task<IssuedSession> StartAsync(Guid userId, string sessionStamp, SessionOrigin origin, CancellationToken cancellationToken)
    {
        var now = Now;
        var limits = options.Value;

        // Keeps the table small: this user's sessions that ended or expired over a month ago.
        var longGone = now.AddDays(-limits.MaxDays);
        await db.UserSessions
            .Where(s => s.UserId == userId && ((s.RevokedAt != null && s.RevokedAt < longGone) || s.AbsoluteExpiresAt < longGone))
            .ExecuteDeleteAsync(cancellationToken);

        var token = NewToken();
        var session = new UserSession
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = Hash(token),
            SessionStamp = sessionStamp,
            CreatedAt = now,
            AbsoluteExpiresAt = now.AddDays(limits.MaxDays),
            SignInMethod = origin.Method,
            UserAgent = origin.UserAgent is { Length: > UserSession.MaxUserAgentLength } agent
                ? agent[..UserSession.MaxUserAgentLength]
                : origin.UserAgent,
        };
        session.ExpiresAt = IdleExpiry(session, now);
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        accessCache.Forget(userId);

        return new IssuedSession(session.Id, token, session.ExpiresAt);
    }

    public async Task<SessionRenewal?> RenewAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var session = await FindAsync(refreshToken, cancellationToken);
        if (session is null || session.RevokedAt is not null)
        {
            return null;
        }

        var now = Now;
        var presentedCurrent = session.TokenHash == Hash(refreshToken);
        var replacedLongAgo = !presentedCurrent
            && (session.RotatedAt is not { } rotatedAt || now - rotatedAt > TimeSpan.FromSeconds(options.Value.ReuseGraceSeconds));
        if (replacedLongAgo || session.ExpiresAt <= now || session.AbsoluteExpiresAt <= now)
        {
            // A replaced token used again is a copy: nobody keeps using a session that may be stolen.
            await EndAsync(session, cancellationToken);
            return null;
        }

        // Replaced only if nobody has replaced it since it was read. Otherwise two renewals with
        // the same token would both replace it, and the browser could end up holding the one
        // the database has already forgotten.
        var token = NewToken();
        var tokenHash = Hash(token);
        var expiresAt = IdleExpiry(session, now);
        var replaced = await db.UserSessions
            .Where(s => s.Id == session.Id && s.TokenHash == session.TokenHash && s.RevokedAt == null)
            .ExecuteUpdateAsync(
                set => set
                    .SetProperty(s => s.PreviousTokenHash, session.TokenHash)
                    .SetProperty(s => s.TokenHash, tokenHash)
                    .SetProperty(s => s.RotatedAt, now)
                    .SetProperty(s => s.ExpiresAt, expiresAt),
                cancellationToken);
        if (replaced == 1)
        {
            return new SessionRenewal(session.UserId, session.SessionStamp, new IssuedSession(session.Id, token, expiresAt));
        }

        // The session changed since it was read. If it ended, so does this renewal. If another
        // renewal with the same token got there a moment earlier - two tabs, or a browser
        // without Web Locks - its answer takes the new token to the same browser, so this one
        // hands out an access token and leaves the cookie alone: a second new token could reach
        // the browser last and leave it holding the replaced one.
        var latest = await db.UserSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == session.Id, cancellationToken);
        return presentedCurrent && latest is { RevokedAt: null }
            ? new SessionRenewal(session.UserId, session.SessionStamp, new IssuedSession(session.Id, null, latest.ExpiresAt))
            : null;
    }

    public async Task EndAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var session = await FindAsync(refreshToken, cancellationToken);
        if (session is { RevokedAt: null })
        {
            await EndAsync(session, cancellationToken);
        }
    }

    public async Task EndAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = Now;
        await db.UserSessions
            .Where(s => s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, now), cancellationToken);
        accessCache.Forget(userId);
    }

    // Those a renewal would still accept: not ended, not expired, and begun under the account's
    // current password (a temporary one set by an Admin leaves the older ones to end on their
    // next renewal).
    public async Task<IReadOnlyList<SignInSessionDto>> ListOpenAsync(
        Guid userId, Guid? currentSessionId, CancellationToken cancellationToken)
    {
        var securityStamp = await db.Users
            .Where(u => u.Id == userId)
            .Select(u => u.SecurityStamp)
            .SingleOrDefaultAsync(cancellationToken);
        var stamp = UserAccessChecker.SessionStampOf(securityStamp);
        var now = Now;

        var open = await db.UserSessions
            .AsNoTracking()
            .Where(s => s.UserId == userId
                && s.RevokedAt == null
                && s.ExpiresAt > now
                && s.AbsoluteExpiresAt > now
                && s.SessionStamp == stamp)
            .Select(s => new { s.Id, s.UserAgent, s.SignInMethod, s.CreatedAt, LastActiveAt = s.RotatedAt ?? s.CreatedAt })
            .ToListAsync(cancellationToken);

        return open
            .OrderByDescending(s => s.Id == currentSessionId)
            .ThenByDescending(s => s.LastActiveAt)
            .Select(s => new SignInSessionDto(s.Id, s.UserAgent, s.SignInMethod, s.CreatedAt, s.LastActiveAt, s.Id == currentSessionId))
            .ToList();
    }

    public async Task<bool> EndAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var now = Now;
        var ended = await db.UserSessions
            .Where(s => s.Id == sessionId && s.UserId == userId && s.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, now), cancellationToken);
        if (ended == 0)
        {
            return false;
        }

        // Its access tokens are refused from the next request, and its live connections are cut:
        // the device it was on finds out at once (UserAccessCache.Forget).
        accessCache.Forget(userId);
        return true;
    }

    public async Task<int> EndOthersAsync(Guid userId, Guid keepSessionId, CancellationToken cancellationToken)
    {
        var now = Now;
        var ended = await db.UserSessions
            .Where(s => s.UserId == userId && s.Id != keepSessionId && s.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, now), cancellationToken);
        if (ended > 0)
        {
            accessCache.Forget(userId);
        }

        return ended;
    }

    public static string Hash(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    // The session a refresh token belongs to, as the database has it now: the token may be its
    // current one, or the one it replaced.
    private Task<UserSession?> FindAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = Hash(refreshToken);
        return db.UserSessions
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.TokenHash == hash || s.PreviousTokenHash == hash, cancellationToken);
    }

    private async Task EndAsync(UserSession session, CancellationToken cancellationToken)
    {
        var now = Now;
        await db.UserSessions
            .Where(s => s.Id == session.Id && s.RevokedAt == null)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.RevokedAt, now), cancellationToken);
        accessCache.Forget(session.UserId);
    }

    private DateTime IdleExpiry(UserSession session, DateTime now)
    {
        var idle = now.AddDays(options.Value.IdleDays);
        return idle < session.AbsoluteExpiresAt ? idle : session.AbsoluteExpiresAt;
    }

    private DateTime Now => timeProvider.GetUtcNow().UtcDateTime;

    private static string NewToken() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
}
