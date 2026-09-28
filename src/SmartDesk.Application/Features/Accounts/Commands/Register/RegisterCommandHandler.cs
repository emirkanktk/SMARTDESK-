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
        var managerEmail = string.IsNullOrWhiteSpace(request.ManagerEmail) ? null : request.ManagerEmail.Trim();

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
        else if (managerEmail is null)
        {
            await SignUpManagerAsync(firstName, lastName, email, request.Password, cancellationToken);
        }
        else
        {
            await RequestToJoinTeamAsync(firstName, lastName, email, request.Password, managerEmail, cancellationToken);
        }

        var remaining = MinimumDuration - timeProvider.GetElapsedTime(started);
        if (remaining > TimeSpan.Zero)
        {
            await Task.Delay(remaining, timeProvider, cancellationToken);
        }
    }

    private async Task SignUpManagerAsync(
        string firstName, string lastName, string email, string password, CancellationToken cancellationToken)
    {
        await CreatePendingAsync(firstName, lastName, email, password, Roles.Manager, null, cancellationToken);
        emails.Queue(AccountEmails.SignUpReceived(email, firstName));

        foreach (var admin in await ActiveAdminsAsync(cancellationToken))
        {
            emails.Queue(AccountEmails.NewSignUpForAdmin(admin.Email, admin.FirstName, $"{firstName} {lastName}", email, links.Users()));
        }
    }

    // The manager decides; if the email isn't an active manager's, an Admin does. The applicant
    // gets the same email either way, so this can't be used to find out who the managers are.
    private async Task RequestToJoinTeamAsync(
        string firstName, string lastName, string email, string password, string managerEmail, CancellationToken cancellationToken)
    {
        var manager = await accounts.FindByEmailAsync(managerEmail, cancellationToken);
        var team = manager is { Role: Roles.Manager, Status: AccountStatus.Active } ? manager : null;

        await CreatePendingAsync(firstName, lastName, email, password, Roles.Employee, team?.Id, cancellationToken);
        emails.Queue(AccountEmails.JoinRequestReceived(email, firstName));

        var applicant = $"{firstName} {lastName}";
        if (team is not null)
        {
            emails.Queue(AccountEmails.JoinRequestForManager(team.Email, team.FirstName, applicant, email, links.Team()));
            return;
        }

        foreach (var admin in await ActiveAdminsAsync(cancellationToken))
        {
            emails.Queue(AccountEmails.JoinRequestForAdmin(admin.Email, admin.FirstName, applicant, email, managerEmail, links.Users()));
        }
    }

    private async Task CreatePendingAsync(
        string firstName, string lastName, string email, string password, string role, Guid? managerId, CancellationToken cancellationToken)
    {
        var created = await accounts.CreateAsync(
            new NewAccount(firstName, lastName, email, role, AccountStatus.PendingApproval, managerId, password),
            cancellationToken);
        if (!created.Succeeded)
        {
            throw PasswordRejected(created.PasswordErrors);
        }
    }

    private Task<IReadOnlyList<UserAccount>> ActiveAdminsAsync(CancellationToken cancellationToken) =>
        accounts.ListAsync(new UserFilter(AccountStatus.Active, Roles.Admin), cancellationToken);

    private static ValidationException PasswordRejected(IEnumerable<string> errors) =>
        new(errors.Select(error => new ValidationFailure(nameof(RegisterCommand.Password), error)));
}
