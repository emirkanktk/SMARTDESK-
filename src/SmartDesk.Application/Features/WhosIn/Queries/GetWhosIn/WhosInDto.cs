namespace SmartDesk.Application.Features.WhosIn.Queries.GetWhosIn;

// Who's in the office on a day (ADR-0063). HasTeam: the caller is in a team, a Manager's own or
// an Employee's, so the app offers to show just that team.
public sealed record WhosInDto(DateOnly Date, string TimeZone, bool HasTeam, IReadOnlyList<OfficePersonDto> People);

// Someone who's in that day, with what they booked or are invited to, earliest first. InMyTeam:
// they're in the caller's team, the caller included.
public sealed record OfficePersonDto(Guid UserId, string Name, bool IsMe, bool InMyTeam, IReadOnlyList<OfficeVisitDto> Visits);

// One place and time: their own booking, or - Invited - a meeting someone else booked that they
// said they'd come to (ADR-0032), OrganizerName saying whose; null if that account is gone.
// CheckedIn: the booking was checked in (ADR-0030).
public sealed record OfficeVisitDto(
    Guid ReservationId,
    Guid ResourceId,
    string ResourceName,
    string ResourceTypeName,
    string Location,
    DateTime StartDateTime,
    DateTime EndDateTime,
    bool CheckedIn,
    bool Invited,
    string? OrganizerName);
