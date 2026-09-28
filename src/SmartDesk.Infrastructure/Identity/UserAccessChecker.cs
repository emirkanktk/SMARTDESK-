using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence;

namespace SmartDesk.Infrastructure.Identity;

// Is a token's user still allowed in, as the token describes them (ADR-0020)? A token is
// otherwise good until it expires, so without this a disabled user, or an Admin demoted to
// Employee, would keep their old access for up to an hour. One small query per user, cached
// for 30 seconds; UserAccounts drops the entry the moment it changes that user.
public sealed class UserAccessChecker(SmartDeskDbContext db, IMemoryCache cache) : IUserAccessChecker
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public static string CacheKey(Guid userId) => $"user-access:{userId}";

    public async Task<bool> IsCurrentAsync(
        Guid userId, IReadOnlyCollection<string> tokenRoles, CancellationToken cancellationToken)
    {
        var access = await cache.GetOrCreateAsync(CacheKey(userId), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            var status = await db.Users
                .Where(u => u.Id == userId)
                .Select(u => (AccountStatus?)u.Status)
                .SingleOrDefaultAsync(cancellationToken);
            if (status is null)
            {
                return null;
            }

            var roles = await (
                    from userRole in db.UserRoles
                    join role in db.Roles on userRole.RoleId equals role.Id
                    where userRole.UserId == userId
                    select role.Name!)
                .ToListAsync(cancellationToken);
            return new Access(status.Value, roles.ToHashSet(StringComparer.Ordinal));
        });

        return access is { Status: AccountStatus.Active } && access.Roles.SetEquals(tokenRoles);
    }

    private sealed record Access(AccountStatus Status, HashSet<string> Roles);
}
