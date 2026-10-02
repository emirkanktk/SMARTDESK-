using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Meetings.Common;

// Who the caller may invite to a booking (ADR-0032): a Manager their own team's active members,
// an Admin every active account. Employees invite nobody, and nobody invites themselves.
public sealed class Invitees(IUserAccounts accounts, ICurrentUserService currentUser)
{
    public async Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken)
    {
        var callerId = currentUser.UserId ?? throw new UnauthorizedException();
        IReadOnlyList<UserAccount> people;
        if (currentUser.IsInRole(Roles.Admin))
        {
            people = await accounts.ListAsync(new UserFilter(Status: AccountStatus.Active), cancellationToken);
        }
        else if (currentUser.IsInRole(Roles.Manager))
        {
            people = await accounts.ListAsync(new UserFilter(Status: AccountStatus.Active, ManagerId: callerId), cancellationToken);
        }
        else
        {
            return [];
        }

        return people
            .Where(person => person.Id != callerId)
            .OrderBy(person => person.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public bool MayInvite => currentUser.IsInRole(Roles.Admin) || currentUser.IsInRole(Roles.Manager);
}
