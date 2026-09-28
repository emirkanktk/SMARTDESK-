using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Commands.ApproveAccount;

public sealed class ApproveAccountCommandHandler(IUserAccounts accounts, IEmailSender emails, IFrontendLinks links)
    : IRequestHandler<ApproveAccountCommand, UserAccount>
{
    public async Task<UserAccount> Handle(ApproveAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await accounts.FindAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Account", request.Id);
        if (account.Status != AccountStatus.PendingApproval)
        {
            throw new ConflictException("Only a sign-up that's waiting for approval can be approved.");
        }

        await accounts.SetStatusAsync(account.Id, AccountStatus.Active, cancellationToken);
        emails.Queue(AccountEmails.Approved(account.Email, account.FirstName, links.SignIn()));

        return account with { Status = AccountStatus.Active };
    }
}
