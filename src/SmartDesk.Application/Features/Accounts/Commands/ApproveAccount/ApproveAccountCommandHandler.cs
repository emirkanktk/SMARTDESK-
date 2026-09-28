using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Commands.ApproveAccount;

public sealed class ApproveAccountCommandHandler(
    IUserAccounts accounts,
    ICurrentUserService currentUser,
    IEmailSender emails,
    IFrontendLinks links) : IRequestHandler<ApproveAccountCommand, UserAccount>
{
    public async Task<UserAccount> Handle(ApproveAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountAccess.RequireManageableAsync(accounts, currentUser, request.Id, cancellationToken);
        if (account.Status != AccountStatus.PendingApproval)
        {
            throw new ConflictException("Only a sign-up that's waiting for approval can be approved.");
        }

        if (request.ManagerId is { } managerId && managerId != account.ManagerId)
        {
            // A Manager approves into their own team only; moving people is an Admin's call.
            if (!currentUser.IsInRole(Roles.Admin))
            {
                throw new ForbiddenAccessException("Only an administrator can put someone into another team.");
            }

            if (account.Role != Roles.Employee)
            {
                throw new ConflictException("Only employees belong to a team; managers and admins don't.");
            }

            await AccountRules.RequireActiveManagerAsync(accounts, managerId, nameof(request.ManagerId), cancellationToken);
            await accounts.SetManagerAsync(account.Id, managerId, cancellationToken);
        }

        await accounts.SetStatusAsync(account.Id, AccountStatus.Active, cancellationToken);
        emails.Queue(AccountEmails.Approved(account.Email, account.FirstName, links.SignIn()));

        return (await accounts.FindAsync(account.Id, cancellationToken))!;
    }
}
