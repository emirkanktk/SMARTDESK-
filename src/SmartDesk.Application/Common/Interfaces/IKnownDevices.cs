using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Common.Interfaces;

// The browsers each account has signed in on (ADR-0062), so that its owner hears of a sign-in on
// one it hasn't. A browser is known by a random token in a cookie of its own, which outlives its
// sessions; only the token's hash is kept, once for each account that signed in with it.
public interface IKnownDevices
{
    // How long a browser stays known after the last sign-in on it: as long as browsers keep a
    // cookie at most (Chrome's cap), so the cookie and what the server keeps of it go together.
    static TimeSpan RememberedFor => TimeSpan.FromDays(400);

    // Notes that the user signed in on the browser that presented this token (null: it had none)
    // and says whether their account knew it. The answer's token is the one the browser keeps from
    // now on: the one it presented, or a new one when it had none fit to use.
    Task<DeviceCheck> SignedInAsync(Guid userId, string? token, CancellationToken cancellationToken);
}
