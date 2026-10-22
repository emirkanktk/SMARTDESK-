using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Common.Interfaces;

// Passkeys (ADR-0057): signing in with what unlocks the device - Face ID, a fingerprint, Windows
// Hello - instead of a password. The device makes and keeps the key; the server hands the browser
// a challenge each time and checks what the key signed. The token that comes with a challenge
// carries what checking the answer needs, so nothing is kept in between: it's good once, for a few
// minutes, from the address it was asked from, since a passkey belongs to that address's domain.
// An address the app isn't served at gets a ForbiddenAccessException.
public interface IPasskeys
{
    // What the browser makes the user a new passkey with.
    Task<PasskeyChallenge> CreationOptionsAsync(Guid userId, CancellationToken cancellationToken);

    // Checks the passkey the browser made with those options, and keeps it under this name.
    Task<PasskeyAdded> AddAsync(Guid userId, string credentialJson, string token, string name, CancellationToken cancellationToken);

    // The user's passkeys, newest first.
    Task<IReadOnlyList<PasskeyDto>> ListAsync(Guid userId, CancellationToken cancellationToken);

    // False when the user has no passkey with this id.
    Task<bool> RemoveAsync(Guid userId, string passkeyId, CancellationToken cancellationToken);

    // What the browser asks for a passkey with before anyone is known: the person picks theirs.
    Task<PasskeyChallenge> RequestOptionsAsync(CancellationToken cancellationToken);

    // Checks what the browser's passkey signed, and says whose account it is.
    Task<PasskeySignIn> SignInAsync(string credentialJson, string token, CancellationToken cancellationToken);
}
