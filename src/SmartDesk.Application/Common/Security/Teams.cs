using SmartDesk.Domain.Constants;

namespace SmartDesk.Application.Common.Security;

// A team is identified by its Manager's account id (ADR-0020). A Manager's own team is theirs;
// an Employee's is their manager's; Admins, and Employees outside any team, have none.
public static class Teams
{
    public static Guid? TeamOf(Guid userId, IReadOnlyCollection<string> roles, Guid? managerId) =>
        roles.Contains(Roles.Admin) ? null
        : roles.Contains(Roles.Manager) ? userId
        : managerId;
}
