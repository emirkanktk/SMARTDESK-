namespace SmartDesk.Application.Common.Models;

// One email, with a plain-text body for every client and an HTML one for those that render it.
public sealed record EmailMessage(string To, string Subject, string Text, string Html);
