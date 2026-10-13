using System.Linq.Expressions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Common;

// Who may use which resource (ADR-0022), in one place for every query and command:
// - a resource with no teams is shared: everyone may see and book it;
// - one with teams may be seen and booked by those teams' members and managers only;
// - an Admin sees and manages everything.
// A resource someone may not use is treated as not there (404), so its name and schedule
// don't leak to other teams.
public static class ResourceAccess
{
    public static Expression<Func<Resource, bool>> UsableBy(Guid? teamId) =>
        r => !r.Teams.Any() || r.Teams.Any(t => t.TeamId == teamId);

    public static Expression<Func<Reservation, bool>> OnResourceUsableBy(Guid? teamId) =>
        r => !r.Resource.Teams.Any() || r.Resource.Teams.Any(t => t.TeamId == teamId);

    public static Expression<Func<ResourceBlock, bool>> OnBlockedResourceUsableBy(Guid? teamId) =>
        b => !b.Resource.Teams.Any() || b.Resource.Teams.Any(t => t.TeamId == teamId);

    public static IQueryable<Resource> UsableBy(this IQueryable<Resource> resources, ICurrentUserService user) =>
        user.IsInRole(Roles.Admin) ? resources : resources.Where(UsableBy(user.TeamId));

    public static IQueryable<Reservation> OnUsableResources(this IQueryable<Reservation> reservations, ICurrentUserService user) =>
        user.IsInRole(Roles.Admin) ? reservations : reservations.Where(OnResourceUsableBy(user.TeamId));

    public static IQueryable<ResourceBlock> OnUsableResources(this IQueryable<ResourceBlock> blocks, ICurrentUserService user) =>
        user.IsInRole(Roles.Admin) ? blocks : blocks.Where(OnBlockedResourceUsableBy(user.TeamId));

    // The same rule for someone other than the caller, e.g. someone invited, or someone on a
    // waiting list (ADR-0043): `teamIds` are the resource's teams.
    public static bool UsableBy(UserAccount person, IReadOnlyCollection<Guid> teamIds) =>
        teamIds.Count == 0
        || person.Role == Roles.Admin
        || teamIds.Contains(person.Role == Roles.Manager ? person.Id : person.ManagerId ?? Guid.Empty);

    // Who may edit a resource, change its status or block it: an Admin, or the Manager of the
    // only team it belongs to. Shared resources, and ones several teams share, are the Admins'.
    public static bool CanManage(IReadOnlyCollection<Guid> teamIds, ICurrentUserService user) =>
        user.IsInRole(Roles.Admin)
        || (user.IsInRole(Roles.Manager) && teamIds.Count == 1 && teamIds.Single() == user.UserId);
}
