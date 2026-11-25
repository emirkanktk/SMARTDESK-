using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Infrastructure.Persistence;

namespace SmartDesk.Infrastructure.Identity;

// The browsers each account signed in on, in the database (ADR-0062).
public sealed class KnownDevices(SmartDeskDbContext db, TimeProvider timeProvider) : IKnownDevices
{
    // 256 random bits as base64url, like a refresh token: nobody guesses another browser's.
    private const int TokenBytes = 32;
    private const int TokenLength = 43;

    public async Task<DeviceCheck> SignedInAsync(Guid userId, string? token, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;

        // Keeps the table small, as sessions keep theirs: this user's browsers whose cookie has
        // expired too by now.
        var forgotten = now - IKnownDevices.RememberedFor;
        await db.KnownDevices
            .Where(d => d.UserId == userId && d.LastSeenAt < forgotten)
            .ExecuteDeleteAsync(cancellationToken);

        if (IsWellFormed(token))
        {
            var hash = Hash(token);
            var seen = await db.KnownDevices
                .Where(d => d.UserId == userId && d.TokenHash == hash)
                .ExecuteUpdateAsync(set => set.SetProperty(d => d.LastSeenAt, now), cancellationToken);
            if (seen > 0)
            {
                return new DeviceCheck(DeviceFamiliarity.Known, token);
            }

            // Another account's browser, or one this account forgot: it keeps its token.
        }
        else
        {
            token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(TokenBytes));
        }

        var first = !await db.KnownDevices.AnyAsync(d => d.UserId == userId, cancellationToken);
        var device = new KnownDevice
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            TokenHash = Hash(token),
            FirstSeenAt = now,
            LastSeenAt = now,
        };
        db.KnownDevices.Add(device);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Unless another sign-in on this browser added it a moment earlier - and told whoever
            // it had to - this is no race, and the sign-in fails as it would have. Either way the
            // row must not go to the database again with the session that comes next.
            db.Entry(device).State = EntityState.Detached;
            if (!await db.KnownDevices.AnyAsync(d => d.UserId == userId && d.TokenHash == device.TokenHash, cancellationToken))
            {
                throw;
            }

            return new DeviceCheck(DeviceFamiliarity.Known, token);
        }

        return new DeviceCheck(first ? DeviceFamiliarity.First : DeviceFamiliarity.New, token);
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    // Only a token this class could have made is looked up; anything else gets a new one.
    private static bool IsWellFormed([NotNullWhen(true)] string? token) =>
        token is { Length: TokenLength } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
