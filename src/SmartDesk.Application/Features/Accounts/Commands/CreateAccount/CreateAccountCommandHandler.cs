using MediatR;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Accounts.Common;

namespace SmartDesk.Application.Features.Accounts.Commands.CreateAccount;

public sealed class CreateAccountCommandHandler(IUserAccounts accounts, AccountCreation creation)
    : IRequestHandler<CreateAccountCommand, UserAccount>
{
    public async Task<UserAccount> Handle(CreateAccountCommand request, CancellationToken cancellationToken)
    {
        if (request.ManagerId is { } managerId)
        {
            await AccountRules.RequireActiveManagerAsync(accounts, managerId, nameof(request.ManagerId), cancellationToken);
        }

        return await creation.CreateAsync(
            request.FirstName, request.LastName, request.Email, request.Role, request.ManagerId, request.Password, cancellationToken);
    }
}
