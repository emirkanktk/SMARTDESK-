using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Passkeys.Common;

public static class PasskeyLimit
{
    public static ConflictException Reached() =>
        new(string.Format(Messages.Passkey_Full, PasskeyRules.MaxPerUser)) { Code = ConflictCodes.PasskeysFull };
}
