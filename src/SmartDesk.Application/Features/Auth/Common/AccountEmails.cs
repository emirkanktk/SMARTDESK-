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

    // A new account made by an Admin or a Manager. It has no password until its owner sets one.
    public static EmailMessage Invitation(
        string to, string firstName, string invitedBy, string setPasswordLink, string forgotPasswordLink) => Compose(
        to, firstName, "You've been added to SmartDesk",
        new Text($"{invitedBy} created a SmartDesk account for you, to book meeting rooms, desks and equipment. Choose your password within {ResetLinkLifetimeText}:"),
        new Link("Choose your password", setPasswordLink),
        new Text("If the link has expired, ask for a new one on the sign-in page:"),
        new Link("Forgot password", forgotPasswordLink));

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
        new Text("An administrator approved your sign-up. You can sign in now, and add the members of your team from My team."),
        new Link("Sign in", signInLink));

    public static EmailMessage Rejected(string to, string firstName) => Compose(
        to, firstName, "Your SmartDesk sign-up",
        new Text("An administrator reviewed your sign-up and didn't approve it, so no account was created. If you think this is a mistake, contact your office's SmartDesk administrator."));

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
