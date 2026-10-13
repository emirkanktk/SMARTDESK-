using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Application.Features.BookingRules.Common;

// The rules bookings keep to (ADR-0046): the office's hours, each kind's own rules, and how far
// a kind's rules may go - which is also what every booking keeps to.
public sealed record BookingRulesDto(OfficeHoursDto OfficeHours, IReadOnlyList<ResourceTypeRulesDto> Types, RuleBoundsDto Bounds);

// Description: the hours as the request's language writes them, "Mon-Fri 09:00-18:00".
public sealed record OfficeHoursDto(
    string TimeZone,
    TimeOnly OpensAt,
    TimeOnly ClosesAt,
    IReadOnlyList<DayOfWeek> Workdays,
    string Description)
{
    public static OfficeHoursDto From(OfficeHours hours) =>
        new(hours.TimeZone, hours.OpensAt, hours.ClosesAt, RecurrenceRules.ToDays(hours.Workdays), OfficeWeek.Of(hours).Describe());
}

// A kind's own rules; one that's null isn't set, and only the rule for every booking applies.
public sealed record ResourceTypeRulesDto(
    Guid ResourceTypeId,
    string Name,
    int? MaxDurationMinutes,
    int? MaxDaysAhead,
    int? MaxActivePerUser,
    bool OfficeHoursOnly)
{
    public static ResourceTypeRulesDto From(ResourceType type) =>
        new(type.Id, type.Name, type.MaxDurationMinutes, type.MaxDaysAhead, type.MaxActivePerUser, type.OfficeHoursOnly);
}

// The most a kind's rules may be set to. The first two are also the rules for every booking
// (ReservationRules); nobody has a limit per person unless a kind sets one.
public sealed record RuleBoundsDto(int MaxDurationMinutes, int MaxDaysAhead, int MaxActivePerUser)
{
    public static RuleBoundsDto Current { get; } = new(
        RuleBounds.MaxDurationMinutes, RuleBounds.MaxDaysAhead, RuleBounds.MaxActivePerUser);
}

// What an Admin may set a kind's rules to: never past the rules for every booking, so a booking
// a kind's rules allow is one any booking may be. Durations and office hours in quarter hours,
// the steps the booking form works in.
public static class RuleBounds
{
    public const int Step = 15;
    public const int MinDurationMinutes = Step;
    public static readonly int MaxDurationMinutes = (int)ReservationRules.MaxDuration.TotalMinutes;
    public const int MaxDaysAhead = ReservationRules.MaxAdvanceDays;
    public const int MaxActivePerUser = 100;

    public static bool IsDuration(int? minutes) =>
        minutes is null || (minutes >= MinDurationMinutes && minutes <= MaxDurationMinutes && minutes % Step == 0);

    public static bool IsDaysAhead(int? days) => days is null or (>= 0 and <= MaxDaysAhead);

    public static bool IsActiveLimit(int? count) => count is null or (>= 1 and <= MaxActivePerUser);

    public static bool IsOnQuarterHour(TimeOnly time) => time.Ticks % TimeSpan.FromMinutes(Step).Ticks == 0;
}
