using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Commands.ChangeAccountRole;

public sealed class ChangeAccountRoleCommandHandler(IUserAccounts accounts, ICurrentUserService currentUser)
    : IRequestHandler<ChangeAccountRoleCommand, UserAccount>
{
    public async Task<UserAccount> Handle(ChangeAccountRoleCommand request, CancellationToken cancellationToken)
    {
        var callerId = currentUser.UserId ?? throw new UnauthorizedException();
        var account = await accounts.FindAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Account", request.Id);

        if (account.Id == callerId)
        {
            throw new ConflictException("You can't change your own role. Ask another admin.");
        }

        if (account.Status == AccountStatus.PendingApproval)
        {
            throw new ConflictException("This sign-up is waiting for approval. Approve or reject it first.");
        }

        if (account.Role == request.Role)
        {
            return account;
        }

        // Their team would be left without a Manager, so it has to go somewhere first.
        if (account is { Role: Roles.Manager, TeamSize: > 0 })
        {
            throw new ConflictException(
                $"{account.DisplayName} still has {account.TeamSize} team member(s). Move them to another manager or remove them from the team first.");
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
