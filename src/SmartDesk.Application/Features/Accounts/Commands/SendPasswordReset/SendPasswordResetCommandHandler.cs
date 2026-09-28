using MediatR;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Accounts.Common;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Commands.SendPasswordReset;

public sealed class SendPasswordResetCommandHandler(
    IUserAccounts accounts,
    ICurrentUserService currentUser,
    IIdentityService identityService,
    IEmailSender emails,
    IFrontendLinks links) : IRequestHandler<SendPasswordResetCommand>
{
    public async Task Handle(SendPasswordResetCommand request, CancellationToken cancellationToken)
    {
        var account = await AccountAccess.RequireManageableAsync(accounts, currentUser, request.Id, cancellationToken);
        if (account.Status != AccountStatus.Active)
        {
            throw new ConflictException("Only an active account can be sent a password reset link.");
        }

        var reset = await identityService.CreatePasswordResetTokenAsync(account.Email, cancellationToken)
            ?? throw new NotFoundException("Account", request.Id);
        emails.Queue(AccountEmails.PasswordReset(reset.User, links.ResetPassword(reset.User.Email, reset.Token)));
    }
}
