using MailKit.Security;

namespace SmartDesk.Infrastructure.Email;

// Where outgoing mail goes (ADR-0019). Without Smtp:Host, emails are only written to the log -
// handy on a machine with no mail server, and never silently lost.
public sealed class EmailOptions
{
    public const string SectionName = "Email";

    public string From { get; init; } = "SmartDesk <no-reply@smartdesk.local>";

    public SmtpOptions Smtp { get; init; } = new();

    public sealed class SmtpOptions
    {
        public string Host { get; init; } = string.Empty;
        public int Port { get; init; } = 25;

        // Auto: TLS straight away on port 465, STARTTLS when the server offers it otherwise,
        // plain for a local catcher like Mailpit that offers neither.
        public SecureSocketOptions Security { get; init; } = SecureSocketOptions.Auto;

        // Credentials, when the server wants them: environment variables or a secret store,
        // never appsettings (ADR-0005).
        public string Username { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
    }
}
