using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Commands.ChangeAccountRole;

public sealed class ChangeAccountRoleCommandHandler(IUserAccounts accounts, ICurrentUserService currentUser, IApplicationDbContext db)
    : IRequestHandler<ChangeAccountRoleCommand, UserAccount>
{
    public async Task<UserAccount> Handle(ChangeAccountRoleCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.UserId ?? throw new UnauthorizedException();
        var account = await accounts.FindAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Account", request.Id);

        if (account.Id == callerId)
        {
            throw new ConflictException(Messages.Accounts_OwnRole);
        }

        if (account.Status == AccountStatus.PendingApproval)
        {
            throw new ConflictException(Messages.Accounts_PendingDecideFirst);
        }

        if (account.Role == request.Role)
        {
            return account;
        }

        // Their team would be left without a Manager, so it has to go somewhere first.
        if (account is { Role: Roles.Manager, TeamSize: > 0 })
        {
            throw new ConflictException(
                string.Format(Messages.Accounts_ManagerHasMembers, account.DisplayName, account.TeamSize));
        }

        // Their team's resources would belong to nobody (ADR-0022).
        if (account.Role == Roles.Manager)
        {
            var teamResources = await db.ResourceTeams.CountAsync(t => t.TeamId == account.Id, cancellationToken);
            if (teamResources > 0)
            {
                throw new ConflictException(
                    string.Format(Messages.Accounts_ManagerHasResources, account.DisplayName, teamResources));
            }
        }

        await LastAdminGuard.ChangeAsync(
            accounts,
            account,
            () => accounts.SetRoleAsync(account.Id, request.Role, cancellationToken),
            () => accounts.SetRoleAsync(account.Id, account.Role, CancellationToken.None),
            cancellationToken);

        // Only Employees sit in a team.
        if (request.Role != Roles.Employee && account.ManagerId is not null)
        {
            await accounts.SetManagerAsync(account.Id, null, cancellationToken);
        }

        return (await accounts.FindAsync(account.Id, cancellationToken))!;
    }
}
