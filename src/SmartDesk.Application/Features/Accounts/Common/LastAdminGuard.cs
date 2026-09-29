using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Common;

// The last active Admin can't be demoted or switched off: nobody could manage accounts any more
// (ADR-0020). Counted before the change and again after it, because two Admins demoting each
// other at the same moment would both pass the first count. Whoever then finds no Admin left
// undoes their own change, so at least one Admin always remains.
public static class LastAdminGuard
{
    public static string Message => Messages.Accounts_LastAdmin;

    public static async Task ChangeAsync(
        IUserAccounts accounts,
        UserAccount account,
        Func<Task> change,
        Func<Task> undo,
        CancellationToken cancellationToken)
    {
        var isActiveAdmin = account is { Role: Roles.Admin, Status: AccountStatus.Active };
        if (isActiveAdmin && await CountActiveAdminsAsync(accounts, cancellationToken) <= 1)
        {
            throw new ConflictException(Message);
        }

        await change();

        if (isActiveAdmin && await CountActiveAdminsAsync(accounts, CancellationToken.None) == 0)
        {
            await undo();
            throw new ConflictException(Message);
        }
    }

    private static Task<int> CountActiveAdminsAsync(IUserAccounts accounts, CancellationToken cancellationToken) =>
        accounts.CountAsync(Roles.Admin, AccountStatus.Active, cancellationToken);
}
