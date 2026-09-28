using MediatR;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Accounts.Common;

namespace SmartDesk.Application.Features.Accounts.Commands.CreateAccount;

public sealed class CreateAccountCommandHandler(IUserAccounts accounts, AccountInvitations invitations)
    : IRequestHandler<CreateAccountCommand, UserAccount>
{
    public async Task<UserAccount> Handle(CreateAccountCommand request, CancellationToken cancellationToken)
    {
        if (request.ManagerId is { } managerId)
        {
            await AccountRules.RequireActiveManagerAsync(accounts, managerId, nameof(request.ManagerId), cancellationToken);
        }

        return await invitations.InviteAsync(
            request.FirstName, request.LastName, request.Email, request.Role, request.ManagerId, cancellationToken);
    }
}
