using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SmartDesk.Application.Common.Interfaces;
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

    public async Task<IssuedSession> StartAsync(Guid userId, string sessionStamp, CancellationToken cancellationToken)
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
        };
        session.ExpiresAt = IdleExpiry(session, now);
        db.UserSessions.Add(session);
        await db.SaveChangesAsync(cancellationToken);
        accessCache.Forget(userId);

        return new IssuedSession(session.Id, token, session.ExpiresAt);
    }

    public async Task<SessionRenewal?> RenewAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = Hash(refreshToken);
        var session = await db.UserSessions
            .SingleOrDefaultAsync(s => s.TokenHash == hash || s.PreviousTokenHash == hash, cancellationToken);
        if (session is null || session.RevokedAt is not null)
        {
            return null;
        }

        var now = Now;
        var replacedLongAgo = session.TokenHash != hash
            && (session.RotatedAt is not { } rotatedAt || now - rotatedAt > TimeSpan.FromSeconds(options.Value.ReuseGraceSeconds));
        if (replacedLongAgo || session.ExpiresAt <= now || session.AbsoluteExpiresAt <= now)
        {
            // A replaced token used again is a copy: nobody keeps using a session that may be stolen.
            await EndAsync(session, cancellationToken);
            return null;
        }

        var token = NewToken();
        session.PreviousTokenHash = session.TokenHash;
        session.TokenHash = Hash(token);
        session.RotatedAt = now;
        session.ExpiresAt = IdleExpiry(session, now);
        await db.SaveChangesAsync(cancellationToken);

        return new SessionRenewal(session.UserId, session.SessionStamp, new IssuedSession(session.Id, token, session.ExpiresAt));
    }

    public async Task EndAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var hash = Hash(refreshToken);
        var session = await db.UserSessions
            .SingleOrDefaultAsync(s => s.TokenHash == hash || s.PreviousTokenHash == hash, cancellationToken);
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

    public static string Hash(string refreshToken) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken)));

    private async Task EndAsync(UserSession session, CancellationToken cancellationToken)
    {
        session.RevokedAt = Now;
        await db.SaveChangesAsync(cancellationToken);
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
