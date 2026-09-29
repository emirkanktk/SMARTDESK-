using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Common;

public static class ResourceManagement
{
    public static string NotYoursMessage => Messages.Resources_NotYours;

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
