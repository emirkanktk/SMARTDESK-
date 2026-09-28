using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Tests.TestSupport;

public sealed class CapturingEmailSender : IEmailSender
{
    public List<EmailMessage> Sent { get; } = [];

    public void Queue(EmailMessage message) => Sent.Add(message);

    // The one link in an email's text body that starts with this address.
    public static string LinkIn(EmailMessage email, string startingWith) =>
        email.Text.Split('\n').Select(line => line.Trim()).Single(line => line.StartsWith(startingWith, StringComparison.Ordinal));
}
