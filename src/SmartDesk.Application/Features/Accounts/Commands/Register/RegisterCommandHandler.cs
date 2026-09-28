using FluentValidation;
using FluentValidation.Results;
using MediatR;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Accounts.Commands.Register;

public sealed class RegisterCommandHandler(
    IUserAccounts accounts,
    IEmailSender emails,
    IFrontendLinks links,
    TimeProvider timeProvider) : IRequestHandler<RegisterCommand>
{
    // Creating an account writes to the database and a taken email doesn't, so the two answer
    // in measurably different times (about 75 ms against 60 locally). Both wait until this
    // long after the request began, which is well above either, so the time says nothing.
    public static readonly TimeSpan MinimumDuration = TimeSpan.FromMilliseconds(400);

    public async Task Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();
        var email = request.Email.Trim();

        // Before the email is looked up, so a weak password is refused the same way either way.
        var passwordErrors = await accounts.CheckPasswordRulesAsync(request.Password);
        if (passwordErrors.Count > 0)
        {
            throw PasswordRejected(passwordErrors);
        }

        if (await accounts.EmailExistsAsync(email, cancellationToken))
        {
            // Same answer as a new sign-up; the owner hears about it.
            accounts.HashDecoyPassword(request.Password);
            emails.Queue(AccountEmails.AlreadyRegistered(email, firstName, links.SignIn(), links.ForgotPassword()));
        }
        else
        {
            await CreateAsync(firstName, lastName, email, request.Password, cancellationToken);
        }

        var remaining = MinimumDuration - timeProvider.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, timeProvider, cancellationToken);
        }
    }

    private async Task CreateAsync(string firstName, string lastName, string email, string password, CancellationToken cancellationToken)
    {
        var created = await accounts.CreateAsync(
            new NewAccount(firstName, lastName, email, Roles.Manager, AccountStatus.PendingApproval, null, password),
            cancellationToken);
        if (!created.Succeeded)
        {
            throw PasswordRejected(created.PasswordErrors);
        }

        emails.Queue(AccountEmails.SignUpReceived(email, firstName));

        var applicant = $"{firstName} {lastName}";
        var admins = await accounts.ListAsync(new UserFilter(AccountStatus.Active, Roles.Admin), cancellationToken);
        foreach (var admin in admins)
        {
            emails.Queue(AccountEmails.NewSignUpForAdmin(admin.Email, admin.FirstName, applicant, email, links.Users()));
        }
    }

    private static ValidationException PasswordRejected(IEnumerable<string> errors) =>
        new(errors.Select(error => new ValidationFailure(nameof(RegisterCommand.Password), error)));
}
