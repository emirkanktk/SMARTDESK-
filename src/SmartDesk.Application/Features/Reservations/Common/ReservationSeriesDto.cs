using System.Text.Json.Serialization;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reservations.Common;

// How a booking repeats, as asked for (ADR-0035). Interval: every 1 to 4 days or weeks.
// DaysOfWeek: the days of a weekly series - the day it starts on has to be one of them, and is
// the only one when none are given; ignored for a daily one. Until: the last day it may come
// back on, inclusive. TimeZone: the IANA zone its local times are in, e.g. "Europe/Istanbul".
public sealed record Recurrence(
    RecurrenceFrequency Frequency,
    int Interval,
    IReadOnlyList<DayOfWeek>? DaysOfWeek,
    DateOnly Until,
    string TimeZone);

// The rule a booking repeats by, on each of its dates: enough for a screen to say "every Monday
// until 30 Nov". Days is stored as one number; DaysOfWeek lists them.
public sealed record ReservationSeriesDto(
    Guid Id,
    RecurrenceFrequency Frequency,
    int Interval,
    [property: JsonIgnore] WeekDays Days,
    DateOnly Until,
    string TimeZone)
{
    public IReadOnlyList<DayOfWeek> DaysOfWeek => RecurrenceRules.ToDays(Days);
}

// One date of a series and whether it can be booked. Code and Reason say why not: a blocked
// window over it, or another booking (ConflictCodes.ResourceBlocked, ReservationOverlaps).
public sealed record SeriesDateDto(DateTime StartDateTime, DateTime EndDateTime, bool Free, string? Code, string? Reason);

// What booking a series did: its rule, the dates booked, and the dates skipped because they
// weren't free.
public sealed record ReservationSeriesResultDto(
    ReservationSeriesDto Series,
    IReadOnlyList<ReservationDto> Booked,
    IReadOnlyList<SeriesDateDto> Skipped);
