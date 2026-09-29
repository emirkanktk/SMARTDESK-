using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Constants;

namespace SmartDesk.Application.Features.Accounts.Commands.AssignManager;

public sealed class AssignManagerCommandHandler(IUserAccounts accounts)
    : IRequestHandler<AssignManagerCommand, UserAccount>
{
    public async Task<UserAccount> Handle(AssignManagerCommand request, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Account", request.Id);
        if (account.Role != Roles.Employee)
        {
            throw new ConflictException(Messages.Accounts_OnlyEmployeesInTeams);
        }

        if (request.ManagerId is { } managerId)
        {
            await AccountRules.RequireActiveManagerAsync(accounts, managerId, nameof(request.ManagerId), cancellationToken);
        }

        if (account.ManagerId == request.ManagerId)
        {
            return account;
        }

        await accounts.SetManagerAsync(account.Id, request.ManagerId, cancellationToken);
        return (await accounts.FindAsync(account.Id, cancellationToken))!;
    }
}
