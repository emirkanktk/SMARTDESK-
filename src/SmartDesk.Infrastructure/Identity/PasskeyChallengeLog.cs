using System.Collections.Concurrent;

namespace SmartDesk.Infrastructure.Identity;

// The passkey challenges already answered (ADR-0057), so each is good for one answer: a copy of a
// sign-in that went through can't sign in again while its token would still be good. Kept in
// memory until then, like the access checks' cache - with several API instances, a copy could be
// used once on each of the others.
public sealed class PasskeyChallengeLog(TimeProvider timeProvider)
{
    private readonly ConcurrentDictionary<string, DateTimeOffset> _answered = new();

    // False when the challenge was answered already.
    public bool TryUse(string challengeId, DateTimeOffset goodUntil)
    {
        var now = timeProvider.GetUtcNow();
        foreach (var (id, until) in _answered)
        {
            if (until <= now)
            {
                _answered.TryRemove(id, out _);
            }
        }

        return _answered.TryAdd(challengeId, goodUntil);
    }
}
