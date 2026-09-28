using FluentValidation;
using FluentValidation.Results;
using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Commands.SetTemporaryPassword;

public sealed class SetTemporaryPasswordCommandHandler(
    IUserAccounts accounts,
    ICurrentUserService currentUser,
    IEmailSender emails) : IRequestHandler<SetTemporaryPasswordCommand, UserAccount>
{
    public async Task<UserAccount> Handle(SetTemporaryPasswordCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountAccess.RequireManageableAsync(accounts, currentUser, request.Id, cancellationToken);

        if (account.Id == currentUser.UserId)
        {
            throw new ConflictException("Change your own password from your account instead.");
        }

        if (account.Status == AccountStatus.PendingApproval)
        {
            throw new ConflictException("This sign-up is waiting for approval. Approve or reject it instead.");
        }

        var errors = await accounts.SetTemporaryPasswordAsync(account.Id, request.Password, cancellationToken);
        if (errors.Count > 0)
        {
            throw new ValidationException(errors.Select(error => new ValidationFailure(nameof(request.Password), error)));
        }

        var caller = await accounts.FindAsync(currentUser.UserId!.Value, cancellationToken);
        emails.Queue(AccountEmails.TemporaryPasswordSet(account.Email, account.FirstName, caller?.DisplayName ?? "An administrator"));

        return (await accounts.FindAsync(account.Id, cancellationToken))!;
    }
}
