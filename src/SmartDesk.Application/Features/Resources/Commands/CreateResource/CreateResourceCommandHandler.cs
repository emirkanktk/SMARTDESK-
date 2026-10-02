using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Resources.Commands.CreateResource;

public sealed class CreateResourceCommandHandler(
    IApplicationDbContext db,
    ICurrentUserService currentUser,
    IUserAccounts accounts,
    ResourceReader reader) : IRequestHandler<CreateResourceCommand, ResourceDto>
{
    public async Task<ResourceDto> Handle(CreateResourceCommand request, CancellationToken cancellationToken)
    {
        var teamIds = await TeamsForAsync(request.TeamIds, cancellationToken);

        var resource = new Resource
        {
            Name = request.Name.Trim(),
            ResourceTypeId = request.ResourceTypeId,
            Capacity = request.Capacity,
            Location = request.Location.Trim(),
            Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
            Status = ResourceStatus.Active,
            CheckInRequired = request.CheckInRequired,
            Features = ResourceFeatureNames.Normalize(request.Features)
                .Select(name => new ResourceFeature { Name = name })
                .ToList(),
            Teams = teamIds.Select(id => new ResourceTeam { TeamId = id }).ToList(),
        };

        db.Resources.Add(resource);
        await db.SaveChangesAsync(cancellationToken);

        return (await reader.FindAsync(db.Resources.AsNoTracking().Where(r => r.Id == resource.Id), cancellationToken))!;
    }

    private async Task<IReadOnlyList<Guid>> TeamsForAsync(IReadOnlyList<Guid>? requested, CancellationToken cancellationToken)
    {
        if (currentUser.IsInRole(Roles.Admin))
        {
            return await ResourceTeamRules.RequireActiveManagersAsync(accounts, requested, cancellationToken);
        }

        // A Manager adds to their own team, nobody else's (ADR-0022).
        var managerId = currentUser.UserId ?? throw new UnauthorizedException();
        if (requested is { Count: > 0 } && requested.Any(id => id != managerId))
        {
            throw new ForbiddenAccessException(Messages.Resources_OwnTeamOnly);
        }

        return [managerId];
    }
}
