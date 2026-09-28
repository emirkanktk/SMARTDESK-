using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Commands.RejectAccount;

public sealed class RejectAccountCommandHandler(IUserAccounts accounts, ICurrentUserService currentUser, IEmailSender emails)
    : IRequestHandler<RejectAccountCommand>
{
    public async Task Handle(RejectAccountCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountAccess.RequireManageableAsync(accounts, currentUser, request.Id, cancellationToken);

        // Only a sign-up that was never let in is deleted: it has no reservations or team. An
        // account that has been used is disabled instead, which keeps its history.
        if (account.Status != AccountStatus.PendingApproval)
        {
            throw new ConflictException("Only a sign-up that's waiting for approval can be rejected. Disable the account instead.");
        }

        await accounts.DeleteAsync(account.Id, cancellationToken);
        emails.Queue(AccountEmails.Rejected(account.Email, account.FirstName));
    }
}
