using FluentValidation;
using FluentValidation.Results;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Resources.Common;

public static class ResourceTeamRules
{
    // More than enough for an office; keeps a request from naming thousands of teams.
    public const int MaxTeams = 20;

    // A resource can only be given to teams that exist: each id must be an active Manager's.
    // Reported as a 400 on TeamIds, since it's part of the request body.
    public static async Task<IReadOnlyList<Guid>> RequireActiveManagersAsync(
        IUserAccounts accounts, IReadOnlyCollection<Guid>? teamIds, CancellationToken cancellationToken)
    {
        var distinct = (teamIds ?? []).Distinct().ToList();
        foreach (var teamId in distinct)
        {
            var manager = await accounts.FindAsync(teamId, cancellationToken);
            if (manager is not { Role: Roles.Manager, Status: AccountStatus.Active })
            {
                throw new ValidationException([new ValidationFailure("TeamIds", Messages.Resources_TeamsMustBeActive)]);
            }
        }

        return distinct;
    }

    public static IRuleBuilderOptions<T, IReadOnlyList<Guid>?> TeamList<T>(this IRuleBuilder<T, IReadOnlyList<Guid>?> rule) =>
        rule.Must(ids => ids is null || ids.Count <= MaxTeams)
            .WithMessage(_ => string.Format(Messages.Resources_MaxTeams, MaxTeams))
            .Must(ids => ids is null || ids.All(id => id != Guid.Empty))
            .WithMessage(_ => Messages.Resources_EmptyTeamId);
}
