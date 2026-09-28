using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.Resources.Commands.SetResourceTeams;

public sealed class SetResourceTeamsCommandHandler(IApplicationDbContext db, IUserAccounts accounts, ResourceReader reader)
    : IRequestHandler<SetResourceTeamsCommand, ResourceDto>
{
    public async Task<ResourceDto> Handle(SetResourceTeamsCommand request, CancellationToken cancellationToken)
    {
        var resource = await db.Resources
            .Include(r => r.Teams)
            .SingleOrDefaultAsync(r => r.Id == request.Id, cancellationToken)
            ?? throw new NotFoundException(nameof(Resource), request.Id);

        var teamIds = await ResourceTeamRules.RequireActiveManagersAsync(accounts, request.TeamIds, cancellationToken);

        foreach (var team in resource.Teams.Where(t => !teamIds.Contains(t.TeamId)).ToList())
        {
            resource.Teams.Remove(team);
        }

        foreach (var teamId in teamIds.Where(id => resource.Teams.All(t => t.TeamId != id)))
        {
            resource.Teams.Add(new ResourceTeam { ResourceId = resource.Id, TeamId = teamId });
        }

        await db.SaveChangesAsync(cancellationToken);

        return (await reader.FindAsync(db.Resources.AsNoTracking().Where(r => r.Id == resource.Id), cancellationToken))!;
    }
}
