using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Infrastructure.Email;

// The last step of sending: hands one message to a mail server, or to the log.
public interface IEmailTransport
{
    Task SendAsync(EmailMessage message, CancellationToken cancellationToken);
}

public sealed class SmtpEmailTransport(IOptions<EmailOptions> options) : IEmailTransport
{
    public async Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        var email = options.Value;
        var mime = new MimeMessage
        {
            Subject = message.Subject,
            Body = new BodyBuilder { TextBody = message.Text, HtmlBody = message.Html }.ToMessageBody(),
        };
        mime.From.Add(MailboxAddress.Parse(email.From));
        mime.To.Add(MailboxAddress.Parse(message.To));

        using var client = new SmtpClient();
        await client.ConnectAsync(email.Smtp.Host, email.Smtp.Port, email.Smtp.Security, cancellationToken);
        if (!string.IsNullOrEmpty(email.Smtp.Username))
        {
            await client.AuthenticateAsync(email.Smtp.Username, email.Smtp.Password, cancellationToken);
        }

        await client.SendAsync(mime, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }
}

// Used when no SMTP server is configured: the whole email, link included, goes to the log so
// a developer can still follow the flow.
public sealed class LoggingEmailTransport(ILogger<LoggingEmailTransport> logger) : IEmailTransport
{
    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "No SMTP server configured (Email:Smtp:Host); email to {To}, \"{Subject}\":{NewLine}{Text}",
            message.To, message.Subject, Environment.NewLine, message.Text);
        return Task.CompletedTask;
    }
}
