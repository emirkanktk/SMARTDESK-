using System.Net;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Auth.Common;

// The account emails SmartDesk sends. Names and links are HTML-encoded in the HTML body, so a
// name like "<b>Ayşe</b>" arrives as text, not markup.
public static class AccountEmails
{
    // How long a reset link works. Infrastructure sets Identity's token lifespan from this,
    // so the email can't promise longer than the token lasts.
    public static readonly TimeSpan ResetLinkLifetime = TimeSpan.FromHours(2);

    private static string ResetLinkLifetimeText => $"{(int)ResetLinkLifetime.TotalHours} hours";

    public static EmailMessage PasswordReset(UserProfile user, string link)
    {
        var greeting = Greeting(user);
        return new EmailMessage(
            user.Email,
            "Reset your SmartDesk password",
            $"""
            {greeting}

            Someone asked to reset the password for your SmartDesk account. To choose a new
            password, open this link within {ResetLinkLifetimeText}:

            {link}

            If it wasn't you, ignore this email - your password stays as it is.
            """,
            $"""
            <p>{WebUtility.HtmlEncode(greeting)}</p>
            <p>Someone asked to reset the password for your SmartDesk account. To choose a new
            password, open this link within {ResetLinkLifetimeText}:</p>
            <p><a href="{WebUtility.HtmlEncode(link)}">Choose a new password</a></p>
            <p>If it wasn't you, ignore this email - your password stays as it is.</p>
            """);
    }

    private static string Greeting(UserProfile user) =>
        string.IsNullOrWhiteSpace(user.FirstName) ? "Hello," : $"Hello {user.FirstName},";
}
