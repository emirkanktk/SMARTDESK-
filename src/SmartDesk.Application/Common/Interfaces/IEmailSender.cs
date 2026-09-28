using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Common.Interfaces;

// Hands an email to the background sender and returns at once (ADR-0019). A request never
// waits on the mail server, so neither its speed nor a mail outage can reveal or break
// anything - "forgot password" answers in the same time whether or not the account exists.
public interface IEmailSender
{
    void Queue(EmailMessage message);
}
