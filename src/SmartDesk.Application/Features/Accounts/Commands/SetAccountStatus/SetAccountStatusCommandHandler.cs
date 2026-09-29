using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Commands.SetAccountStatus;

public sealed class SetAccountStatusCommandHandler(IUserAccounts accounts, ICurrentUserService currentUser)
    : IRequestHandler<SetAccountStatusCommand, UserAccount>
{
    public async Task<UserAccount> Handle(SetAccountStatusCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountAccess.RequireManageableAsync(accounts, currentUser, request.Id, cancellationToken);

        if (account.Id == currentUser.UserId)
        {
            throw new ConflictException(Messages.Accounts_CantDisableSelf);
        }

        if (account.Status == AccountStatus.PendingApproval)
        {
            throw new ConflictException(Messages.Accounts_PendingDecideInstead);
        }

        var status = request.Enabled ? AccountStatus.Active : AccountStatus.Disabled;
        if (account.Status == status)
        {
            return account;
        }

        await LastAdminGuard.ChangeAsync(
            accounts,
            account,
            () => accounts.SetStatusAsync(account.Id, status, cancellationToken),
            () => accounts.SetStatusAsync(account.Id, account.Status, CancellationToken.None),
            cancellationToken);

        return account with { Status = status };
    }
}
