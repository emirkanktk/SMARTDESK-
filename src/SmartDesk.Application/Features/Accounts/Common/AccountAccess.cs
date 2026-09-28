using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Domain.Constants;

namespace SmartDesk.Application.Features.Accounts.Common;

// Who may manage whom (ADR-0020): an Admin, anyone; a Manager, only the members of their own
// team. Endpoints already require one of those roles; this narrows a Manager to their team.
public static class AccountAccess
{
    public static async Task<UserAccount> RequireManageableAsync(
        IUserAccounts accounts, ICurrentUserService currentUser, Guid id, CancellationToken cancellationToken)
    {
        var callerId = currentUser.UserId ?? throw new UnauthorizedException();
        var account = await accounts.FindAsync(id, cancellationToken);

        // Not found rather than forbidden, so a Manager can't probe for accounts outside their team.
        if (account is null || (!currentUser.IsInRole(Roles.Admin) && account.ManagerId != callerId))
        {
            throw new NotFoundException("Account", id);
        }

        return account;
    }
}
