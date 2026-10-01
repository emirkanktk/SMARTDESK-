namespace SmartDesk.Application.Features.CheckIn.Common;

// What a resource's QR code holds (ADR-0030): its secret, and the check-in page's address with
// the secret in it, built from App:FrontendUrl like the links in emails.
public sealed record CheckInCodeDto(string Code, string Url);
