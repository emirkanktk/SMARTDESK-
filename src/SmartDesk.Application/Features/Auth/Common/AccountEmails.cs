using System.Globalization;
using System.Net;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Resources;

namespace SmartDesk.Application.Features.Auth.Common;

// The account emails SmartDesk sends (ADR-0019, ADR-0020). Each is built from paragraphs and
// links, into a plain-text and an HTML body. The HTML body encodes every value, so a name like
// "<b>Ayşe</b>" arrives as text, not markup; in the text body each link sits on its own line.
// Every email is written in the culture it's given: the recipient's language (ADR-0023).
public static class AccountEmails
{
    // How long a reset link works. Infrastructure sets Identity's token lifespan from this,
    // so the email can't promise longer than the token lasts.
    public static readonly TimeSpan ResetLinkLifetime = TimeSpan.FromHours(2);

    public static EmailMessage PasswordReset(CultureInfo culture, UserProfile user, string link) => Compose(
        culture, user.Email, user.FirstName, T(culture, nameof(Messages.Email_PasswordReset_Subject)),
        new Text(T(culture, nameof(Messages.Email_PasswordReset_Body),
            T(culture, nameof(Messages.Email_Hours), (int)ResetLinkLifetime.TotalHours))),
        new Link(T(culture, nameof(Messages.Email_PasswordReset_Link)), link),
        new Text(T(culture, nameof(Messages.Email_PasswordReset_NotYou))));

    // A new account made by an Admin or a Manager (ADR-0021). The temporary password is never
    // emailed: the person who chose it hands it over, and the owner replaces it at first sign-in.
    // No creator name means an administrator's account that has since gone.
    public static EmailMessage AccountCreated(CultureInfo culture, string to, string firstName, string? createdBy, string signInLink) => Compose(
        culture, to, firstName, T(culture, nameof(Messages.Email_AccountCreated_Subject)),
        new Text(T(culture, nameof(Messages.Email_AccountCreated_Body),
            createdBy ?? T(culture, nameof(Messages.Email_AnAdministrator)))),
        new Link(T(culture, nameof(Messages.Email_SignIn)), signInLink));

    // Sent whenever someone else sets a temporary password, so the owner knows it happened.
    // No name means an administrator's account that has since gone.
    public static EmailMessage TemporaryPasswordSet(CultureInfo culture, string to, string firstName, string? setBy) => Compose(
        culture, to, firstName, T(culture, nameof(Messages.Email_TemporaryPassword_Subject)),
        new Text(T(culture, nameof(Messages.Email_TemporaryPassword_Body),
            setBy ?? T(culture, nameof(Messages.Email_AnAdministrator)))),
        new Text(T(culture, nameof(Messages.Email_TemporaryPassword_NotExpected))));

    // To an employee who asked to join a team. The same text whether or not the manager's email
    // matched anyone, so it can't be used to find out who the managers are.
    public static EmailMessage JoinRequestReceived(CultureInfo culture, string to, string firstName) => Compose(
        culture, to, firstName, T(culture, nameof(Messages.Email_SignUpReceived_Subject)),
        new Text(T(culture, nameof(Messages.Email_JoinRequestReceived_Body))));

    public static EmailMessage JoinRequestForManager(
        CultureInfo culture, string to, string managerFirstName, string applicantName, string applicantEmail, string teamLink) => Compose(
        culture, to, managerFirstName, T(culture, nameof(Messages.Email_JoinRequestForManager_Subject), applicantName),
        new Text(T(culture, nameof(Messages.Email_JoinRequestForManager_Body), applicantName, applicantEmail)),
        new Link(T(culture, nameof(Messages.Email_ReviewRequest)), teamLink));

    // The manager's email the applicant typed isn't an active manager's, so an Admin decides.
    public static EmailMessage JoinRequestForAdmin(
        CultureInfo culture, string to, string adminFirstName, string applicantName, string applicantEmail, string typedManagerEmail, string usersLink) => Compose(
        culture, to, adminFirstName, T(culture, nameof(Messages.Email_JoinRequestForAdmin_Subject), applicantName),
        new Text(T(culture, nameof(Messages.Email_JoinRequestForAdmin_Body), applicantName, applicantEmail, typedManagerEmail)),
        new Link(T(culture, nameof(Messages.Email_ReviewSignUps)), usersLink));

    public static EmailMessage SignUpReceived(CultureInfo culture, string to, string firstName) => Compose(
        culture, to, firstName, T(culture, nameof(Messages.Email_SignUpReceived_Subject)),
        new Text(T(culture, nameof(Messages.Email_SignUpReceived_Body))));

    // Sent instead of a sign-up confirmation when the email already has an account, so the
    // sign-up form can't tell anyone which emails are registered.
    public static EmailMessage AlreadyRegistered(CultureInfo culture, string to, string firstName, string signInLink, string forgotPasswordLink) => Compose(
        culture, to, firstName, T(culture, nameof(Messages.Email_AlreadyRegistered_Subject)),
        new Text(T(culture, nameof(Messages.Email_AlreadyRegistered_Body))),
        new Link(T(culture, nameof(Messages.Email_SignIn)), signInLink),
        new Text(T(culture, nameof(Messages.Email_AlreadyRegistered_Forgot))),
        new Link(T(culture, nameof(Messages.Email_ForgotPassword)), forgotPasswordLink),
        new Text(T(culture, nameof(Messages.Email_AlreadyRegistered_NotYou))));

    public static EmailMessage NewSignUpForAdmin(
        CultureInfo culture, string to, string adminFirstName, string applicantName, string applicantEmail, string usersLink) => Compose(
        culture, to, adminFirstName, T(culture, nameof(Messages.Email_NewSignUpForAdmin_Subject), applicantName),
        new Text(T(culture, nameof(Messages.Email_NewSignUpForAdmin_Body), applicantName, applicantEmail)),
        new Link(T(culture, nameof(Messages.Email_ReviewSignUps)), usersLink));

    public static EmailMessage Approved(CultureInfo culture, string to, string firstName, string signInLink) => Compose(
        culture, to, firstName, T(culture, nameof(Messages.Email_Approved_Subject)),
        new Text(T(culture, nameof(Messages.Email_Approved_Body))),
        new Link(T(culture, nameof(Messages.Email_SignIn)), signInLink));

    public static EmailMessage Rejected(CultureInfo culture, string to, string firstName) => Compose(
        culture, to, firstName, T(culture, nameof(Messages.Email_Rejected_Subject)),
        new Text(T(culture, nameof(Messages.Email_Rejected_Body))));

    private abstract record Part;

    private sealed record Text(string Value) : Part;

    private sealed record Link(string Label, string Url) : Part;

    // A message in the given culture, not the current request's.
    private static string T(CultureInfo culture, string key, params object[] args)
    {
        var template = Messages.ResourceManager.GetString(key, culture)
            ?? throw new InvalidOperationException($"No message '{key}'.");
        return args.Length == 0 ? template : string.Format(culture, template, args);
    }

    private static EmailMessage Compose(CultureInfo culture, string to, string? firstName, string subject, params Part[] parts)
    {
        var greeting = string.IsNullOrWhiteSpace(firstName)
            ? T(culture, nameof(Messages.Email_GreetingNoName))
            : T(culture, nameof(Messages.Email_Greeting), firstName);

        var text = string.Join("\n\n", parts.Select(part => part switch
        {
            Text t => t.Value,
            Link l => $"{l.Label}:\n{l.Url}",
            _ => string.Empty,
        }).Prepend(greeting));

        var html = string.Join("\n", parts.Select(part => part switch
        {
            Text t => $"<p>{WebUtility.HtmlEncode(t.Value)}</p>",
            Link l => $"<p><a href=\"{WebUtility.HtmlEncode(l.Url)}\">{WebUtility.HtmlEncode(l.Label)}</a></p>",
            _ => string.Empty,
        }).Prepend($"<p>{WebUtility.HtmlEncode(greeting)}</p>"));

        return new EmailMessage(to, subject, text, html);
    }
}
