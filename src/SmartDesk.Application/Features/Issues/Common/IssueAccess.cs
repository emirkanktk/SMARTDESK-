using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Issues.Common;

// Who sees which reported problems (ADR-0037), by the resource's own rules (ADR-0022):
// - those who may use a resource see the problems still open on it, and may report one;
// - those who may manage it - an Admin, or the Manager of the only team it belongs to - see
//   all of its reports and move them on.
public static class IssueAccess
{
    public static IQueryable<IssueReport> OnUsableResources(this IQueryable<IssueReport> issues, ICurrentUserService user)
    {
        if (user.IsInRole(Roles.Admin))
        {
            return issues;
        }

        var teamId = user.TeamId;
        return issues.Where(i => !i.Resource.Teams.Any() || i.Resource.Teams.Any(t => t.TeamId == teamId));
    }

    // Same people as ResourceAccess.CanManage: a team is its Manager's account id.
    public static IQueryable<IssueReport> OnManageableResources(this IQueryable<IssueReport> issues, ICurrentUserService user)
    {
        if (user.IsInRole(Roles.Admin))
        {
            return issues;
        }

        if (user.IsInRole(Roles.Manager) && user.UserId is { } managerId)
        {
            return issues.Where(i => i.Resource.Teams.Count() == 1 && i.Resource.Teams.Any(t => t.TeamId == managerId));
        }

        return issues.Where(_ => false);
    }

    // For a report whose resource was loaded with its Teams.
    public static bool CanManage(IssueReport issue, ICurrentUserService user) =>
        ResourceAccess.CanManage(issue.Resource.Teams.Select(t => t.TeamId).ToList(), user);
}
