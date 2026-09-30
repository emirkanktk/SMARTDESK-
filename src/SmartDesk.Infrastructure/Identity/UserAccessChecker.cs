using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence;

namespace SmartDesk.Infrastructure.Identity;

// The account as it is now, for the checks on every authenticated request (ADR-0020,
// ADR-0021). A token is otherwise good until it expires, so without this a disabled user, a
// demoted Admin or an old session after a password change would keep working for up to an
// hour. One small query per user, cached for 30 seconds; UserAccessCache drops the entry the
// moment SmartDesk changes that user.
public sealed class UserAccessChecker(SmartDeskDbContext db, IMemoryCache cache) : IUserAccessChecker
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public static string CacheKey(Guid userId) => $"user-access:{userId}";

    // What goes into the token instead of the security stamp itself: enough to tell stamps
    // apart, nothing an attacker could use against Identity.
    public static string SessionStampOf(string? securityStamp) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp ?? string.Empty)).AsSpan(0, 12));

    public Task<UserAccessSnapshot?> GetAsync(Guid userId, CancellationToken cancellationToken) =>
        cache.GetOrCreateAsync(CacheKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            var user = await db.Users
                .AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Status, u.SecurityStamp, u.ManagerId, u.MustChangePassword })
                .SingleOrDefaultAsync(cancellationToken);
            if (user is null)
            {
                return null;
            }

            var roles = await (
                    from userRole in db.UserRoles
                    join role in db.Roles on userRole.RoleId equals role.Id
                    where userRole.UserId == userId
                    select role.Name!)
                .ToListAsync(cancellationToken);

            // Sessions not ended; expiry doesn't matter here, as a token lasts minutes and a
            // session days.
            var sessions = await db.UserSessions
                .AsNoTracking()
                .Where(s => s.UserId == userId && s.RevokedAt == null)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
            return new UserAccessSnapshot(
                user.Status,
                roles.ToHashSet(StringComparer.Ordinal),
                SessionStampOf(user.SecurityStamp),
                user.ManagerId,
                user.MustChangePassword,
                sessions.ToHashSet());
        });
}

// Called after every change to an account: the next request with its token is checked
// against the database again, and its real-time connections reconnect under the same check.
public sealed class UserAccessCache(IMemoryCache cache, IRealtimeSessions realtimeSessions)
{
    public void Forget(Guid userId)
    {
        cache.Remove(UserAccessChecker.CacheKey(userId));
        realtimeSessions.End(userId);
    }
}

// Where there is no real-time hub (tests, tools), there are no connections to end.
public sealed class NoRealtimeSessions : IRealtimeSessions
{
    public void End(Guid userId)
    {
    }
}
