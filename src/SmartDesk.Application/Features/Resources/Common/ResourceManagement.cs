using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Common;

public static class ResourceManagement
{
    public const string NotYoursMessage =
        "Only an administrator can change a resource that's shared, or that other teams use too.";

    // For a resource the caller can already see (loaded with its Teams): may they edit it,
    // change its status or block it? 403 otherwise, since they know it exists (ADR-0022).
    public static void RequireManageable(Resource resource, ICurrentUserService user)
    {
        if (!ResourceAccess.CanManage(resource.Teams.Select(t => t.TeamId).ToList(), user))
        {
            throw new ForbiddenAccessException(NotYoursMessage);
        }
    }
}
