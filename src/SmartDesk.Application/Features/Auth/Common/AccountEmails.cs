using System.Net;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Auth.Common;

// The account emails SmartDesk sends (ADR-0019, ADR-0020). Each is built from paragraphs and
// links, into a plain-text and an HTML body. The HTML body encodes every value, so a name like
// "<b>Ayşe</b>" arrives as text, not markup; in the text body each link sits on its own line.
public static class AccountEmails
{
    // How long a reset link works. Infrastructure sets Identity's token lifespan from this,
    // so the email can't promise longer than the token lasts.
    public static readonly TimeSpan ResetLinkLifetime = TimeSpan.FromHours(2);

    private static string ResetLinkLifetimeText => $"{(int)ResetLinkLifetime.TotalHours} hours";

    public static EmailMessage PasswordReset(UserProfile user, string link) => Compose(
        user.Email, user.FirstName, "Reset your SmartDesk password",
        new Text($"Someone asked to reset the password for your SmartDesk account. To choose a new password, open this link within {ResetLinkLifetimeText}:"),
        new Link("Choose a new password", link),
        new Text("If it wasn't you, ignore this email - your password stays as it is."));

    // A new account made by an Admin or a Manager (ADR-0021). The temporary password is never
    // emailed: the person who chose it hands it over, and the owner replaces it at first sign-in.
    public static EmailMessage AccountCreated(string to, string firstName, string createdBy, string signInLink) => Compose(
        to, firstName, "You've been added to SmartDesk",
        new Text($"{createdBy} created a SmartDesk account for you, to book meeting rooms, desks and equipment. {createdBy} will give you a temporary password; when you first sign in, you'll choose your own."),
        new Link("Sign in", signInLink));

    // Sent whenever someone else sets a temporary password, so the owner knows it happened.
    public static EmailMessage TemporaryPasswordSet(string to, string firstName, string setBy) => Compose(
        to, firstName, "Your SmartDesk password was changed",
        new Text($"{setBy} set a temporary password for your SmartDesk account, and you've been signed out everywhere. Ask {setBy} for it; when you next sign in, you'll choose your own."),
        new Text("If you didn't expect this, tell your manager or your office's SmartDesk administrator."));

    // To an employee who asked to join a team. The same text whether or not the manager's email
    // matched anyone, so it can't be used to find out who the managers are.
    public static EmailMessage JoinRequestReceived(string to, string firstName) => Compose(
        to, firstName, "We've received your SmartDesk sign-up",
        new Text("Thanks for signing up. Your request to join your manager's team is waiting for approval, and we'll email you as soon as your account is ready to use."));

    public static EmailMessage JoinRequestForManager(
        string to, string managerFirstName, string applicantName, string applicantEmail, string teamLink) => Compose(
        to, managerFirstName, $"{applicantName} wants to join your team",
        new Text($"{applicantName} ({applicantEmail}) signed up for SmartDesk and asked to join your team. Approve or reject the request under My team."),
        new Link("Review the request", teamLink));

    // The manager's email the applicant typed isn't an active manager's, so an Admin decides.
    public static EmailMessage JoinRequestForAdmin(
        string to, string adminFirstName, string applicantName, string applicantEmail, string typedManagerEmail, string usersLink) => Compose(
        to, adminFirstName, $"New sign-up: {applicantName}",
        new Text($"{applicantName} ({applicantEmail}) signed up and gave {typedManagerEmail} as their manager, but that isn't an active manager's email. Approve them into a team, or reject the sign-up."),
        new Link("Review sign-ups", usersLink));

    public static EmailMessage SignUpReceived(string to, string firstName) => Compose(
        to, firstName, "We've received your SmartDesk sign-up",
        new Text("Thanks for signing up as a manager. An administrator will review your request, and we'll email you as soon as your account is ready to use."));

    // Sent instead of a sign-up confirmation when the email already has an account, so the
    // sign-up form can't tell anyone which emails are registered.
    public static EmailMessage AlreadyRegistered(string to, string firstName, string signInLink, string forgotPasswordLink) => Compose(
        to, firstName, "You already have a SmartDesk account",
        new Text("Someone just tried to sign up for SmartDesk with this email address, but it already has an account. If it was you, sign in:"),
        new Link("Sign in", signInLink),
        new Text("Forgotten the password? Choose a new one:"),
        new Link("Forgot password", forgotPasswordLink),
        new Text("If it wasn't you, you can ignore this email."));

    public static EmailMessage NewSignUpForAdmin(
        string to, string adminFirstName, string applicantName, string applicantEmail, string usersLink) => Compose(
        to, adminFirstName, $"New manager sign-up: {applicantName}",
        new Text($"{applicantName} ({applicantEmail}) signed up as a manager and is waiting for approval."),
        new Link("Review sign-ups", usersLink));

    public static EmailMessage Approved(string to, string firstName, string signInLink) => Compose(
        to, firstName, "Your SmartDesk account is ready",
        new Text("Your sign-up was approved. You can sign in now."),
        new Link("Sign in", signInLink));

    public static EmailMessage Rejected(string to, string firstName) => Compose(
        to, firstName, "Your SmartDesk sign-up",
        new Text("Your sign-up was reviewed and not approved, so no account was created. If you think this is a mistake, contact your manager or your office's SmartDesk administrator."));

    private abstract record Part;

    private sealed record Text(string Value) : Part;

    private sealed record Link(string Label, string Url) : Part;

    private static EmailMessage Compose(string to, string? firstName, string subject, params Part[] parts)
    {
        var greeting = string.IsNullOrWhiteSpace(firstName) ? "Hello," : $"Hello {firstName},";

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
