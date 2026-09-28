using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Queries.GetMyTeam;

public sealed class GetMyTeamQueryHandler(IUserAccounts accounts, ICurrentUserService currentUser)
    : IRequestHandler<GetMyTeamQuery, IReadOnlyList<UserAccount>>
{
    public async Task<IReadOnlyList<UserAccount>> Handle(GetMyTeamQuery request, CancellationToken cancellationToken)
    {
        var managerId = currentUser.UserId ?? throw new UnauthorizedException();
        var team = await accounts.ListAsync(new UserFilter(ManagerId: managerId), cancellationToken);

        return team
            .OrderByDescending(a => a.Status == AccountStatus.PendingApproval)
            .ThenBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(a => a.Email, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
