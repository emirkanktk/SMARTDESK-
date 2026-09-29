using FluentValidation;
using FluentValidation.Results;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Common;

// Creates an account for someone else - an Admin adding anyone, a Manager adding a team member
// (ADR-0021). The creator chooses a temporary password and hands it over; the account is Active
// but its owner must replace the password before doing anything else. The owner gets an email
// saying the account exists - without the password, which never travels by email.
public sealed class AccountCreation(
    IUserAccounts accounts,
    ICurrentUserService currentUser,
    IEmailSender emails,
    IFrontendLinks links)
{
    public async Task<UserAccount> CreateAsync(
        string firstName,
        string lastName,
        string email,
        string role,
        Guid? managerId,
        string temporaryPassword,
        CancellationToken cancellationToken)
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
            new NewAccount(
                firstName.Trim(), lastName.Trim(), email, role, AccountStatus.Active, managerId, temporaryPassword,
                MustChangePassword: true,
                Language: Languages.Current),
            cancellationToken);
        if (!created.Succeeded)
        {
            throw new ValidationException(created.PasswordErrors.Select(error => new ValidationFailure("Password", error)));
        }

        var account = (await accounts.FindAsync(created.Id!.Value, cancellationToken))!;
        var creator = await accounts.FindAsync(callerId, cancellationToken);
        emails.Queue(AccountEmails.AccountCreated(Languages.CultureFor(account.Language), account.Email, account.FirstName, creator?.DisplayName, links.SignIn()));

        return account;
    }
}
