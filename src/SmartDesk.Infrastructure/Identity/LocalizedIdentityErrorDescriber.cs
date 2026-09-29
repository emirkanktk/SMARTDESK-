using Microsoft.AspNetCore.Identity;
using SmartDesk.Application.Resources;

namespace SmartDesk.Infrastructure.Identity;

// Identity's password rules in the request's language (ADR-0023). These are the only Identity
// errors a user reads: the others are turned into SmartDesk's own messages or never happen.
// The English texts in Messages.resx are Identity's own, word for word.
public sealed class LocalizedIdentityErrorDescriber : IdentityErrorDescriber
{
    public override IdentityError PasswordTooShort(int length) =>
        Error(nameof(PasswordTooShort), string.Format(Messages.Identity_PasswordTooShort, length));

    public override IdentityError PasswordRequiresDigit() =>
        Error(nameof(PasswordRequiresDigit), Messages.Identity_PasswordRequiresDigit);

    public override IdentityError PasswordRequiresLower() =>
        Error(nameof(PasswordRequiresLower), Messages.Identity_PasswordRequiresLower);

    public override IdentityError PasswordRequiresUpper() =>
        Error(nameof(PasswordRequiresUpper), Messages.Identity_PasswordRequiresUpper);

    public override IdentityError PasswordRequiresNonAlphanumeric() =>
        Error(nameof(PasswordRequiresNonAlphanumeric), Messages.Identity_PasswordRequiresNonAlphanumeric);

    public override IdentityError PasswordRequiresUniqueChars(int uniqueChars) =>
        Error(nameof(PasswordRequiresUniqueChars), string.Format(Messages.Identity_PasswordRequiresUniqueChars, uniqueChars));

    private static IdentityError Error(string code, string description) => new() { Code = code, Description = description };
}
