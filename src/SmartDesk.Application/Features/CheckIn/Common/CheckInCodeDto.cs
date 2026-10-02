namespace SmartDesk.Application.Features.CheckIn.Common;

// What a resource's QR code holds (ADR-0030): its secret, and the check-in page's address with
// the secret in it, built from App:FrontendUrl like the links in emails. DoorScreenSince: when
// a door screen was set up, whose changing code is then the only one that checks in (ADR-0034).
public sealed record CheckInCodeDto(string Code, string Url, DateTime? DoorScreenSince);

// A door screen's address, with its token: given out once, when it's set up (ADR-0034).
public sealed record DoorScreenLinkDto(string Url, DateTime SetUpAt);
