using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Common;

// Turns resources into ResourceDtos with their teams named (ADR-0022). A team is a Manager's
// account, which lives in Identity, so the names are looked up in one query for the whole list.
public sealed class ResourceReader(IIdentityService identityService)
{
    // Keeps the order of `resources`.
    public async Task<IReadOnlyList<ResourceDto>> ListAsync(IQueryable<Resource> resources, CancellationToken cancellationToken)
    {
        var dtos = await resources.Select(ResourceDto.Projection).ToListAsync(cancellationToken);
        var teamIds = dtos.SelectMany(d => d.Teams).Select(t => t.Id).Distinct().ToList();
        if (teamIds.Count == 0)
        {
            return dtos;
        }

        var names = await identityService.GetDisplayNamesAsync(teamIds, cancellationToken);
        return dtos
            .Select(d => d.Teams.Count == 0
                ? d
                : d with
                {
                    Teams = d.Teams
                        .Select(t => t with { ManagerName = names.GetValueOrDefault(t.Id, ResourceTeamDto.UnknownManager) })
                        .OrderBy(t => t.ManagerName, StringComparer.CurrentCultureIgnoreCase)
                        .ToList(),
                })
            .ToList();
    }

    public async Task<ResourceDto?> FindAsync(IQueryable<Resource> resources, CancellationToken cancellationToken) =>
        (await ListAsync(resources, cancellationToken)).SingleOrDefault();
}
