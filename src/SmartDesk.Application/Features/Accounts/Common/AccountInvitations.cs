using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Common;

// Creates an account for someone else - an Admin adding anyone, a Manager adding a team member -
// and emails its owner a link to choose a password (ADR-0020). The account is Active from the
// start but has no password, so nobody can sign in to it until the owner opens the link.
public sealed class AccountInvitations(
    IUserAccounts accounts,
    IIdentityService identityService,
    ICurrentUserService currentUser,
    IEmailSender emails,
    IFrontendLinks links)
{
    public async Task<UserAccount> InviteAsync(
        string firstName, string lastName, string email, string role, Guid? managerId, CancellationToken cancellationToken)
    {
        var callerId = currentUser.UserId ?? throw new UnauthorizedException();
        email = email.Trim();

        // Unlike the public sign-up, telling a signed-in Admin or Manager that the email is
        // taken is fine: they're adding a colleague, and the answer is what they need to know.
        if (await accounts.EmailExistsAsync(email, cancellationToken))
        {
            throw new ConflictException(AccountRules.EmailTaken);
        }

        var created = await accounts.CreateAsync(
            new NewAccount(firstName.Trim(), lastName.Trim(), email, role, AccountStatus.Active, managerId, Password: null),
            cancellationToken);
        var account = (await accounts.FindAsync(created.Id!.Value, cancellationToken))!;

        var inviter = await accounts.FindAsync(callerId, cancellationToken);
        var reset = await identityService.CreatePasswordResetTokenAsync(account.Email, cancellationToken)
            ?? throw new InvalidOperationException($"No reset token for the account just created ({account.Id}).");
        emails.Queue(AccountEmails.Invitation(
            account.Email,
            account.FirstName,
            inviter?.DisplayName ?? "An administrator",
            links.ResetPassword(account.Email, reset.Token),
            links.ForgotPassword()));

        return account;
    }
}
