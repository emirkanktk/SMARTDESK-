using SmartDesk.Application.Features.Reports.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reports.Queries.GetUsageReport;

// Hours are rounded to 2 decimals and utilization (booked working hours / available working
// hours, 0-1) to 4, after summing, so totals don't drift from the rows. NoShows: bookings released
// because nobody checked in; NoShowRate is them over those plus the ones checked in, the visits
// a check-in was asked for. CancellationRate: bookings cancelled by someone, over all of them
// (ADR-0044).
public sealed record UsageReportDto(
    DateOnly From,
    DateOnly To,
    string TimeZone,
    DateTime FromUtc,
    DateTime ToUtc,
    string WorkingHours,
    double WorkingHoursPerResource,
    UsageTotals Totals,
    IReadOnlyList<ResourceUsage> Resources,
    IReadOnlyList<ResourceTypeUsage> ByType,
    IReadOnlyList<UserUsage> TopUsers,
    IReadOnlyList<WeekdayHours> HeatMap);

public sealed record UsageTotals(
    int Resources,
    int Reservations,
    int Cancelled,
    double BookedHours,
    double WorkingHoursBooked,
    double WorkingHoursAvailable,
    double Utilization,
    int NoShows,
    int CheckedIn,
    double NoShowRate,
    double CancellationRate);

public sealed record ResourceUsage(
    Guid ResourceId,
    string ResourceName,
    Guid ResourceTypeId,
    string ResourceTypeName,
    ResourceStatus Status,
    int Reservations,
    int Cancelled,
    double BookedHours,
    double WorkingHoursBooked,
    double Utilization,
    int NoShows,
    int CheckedIn,
    double NoShowRate);

public sealed record ResourceTypeUsage(
    Guid ResourceTypeId,
    string ResourceTypeName,
    int Resources,
    int Reservations,
    int Cancelled,
    double BookedHours,
    double WorkingHoursBooked,
    double WorkingHoursAvailable,
    double Utilization,
    int NoShows,
    int CheckedIn,
    double NoShowRate);

public sealed record UserUsage(Guid UserId, string? DisplayName, int Reservations, double BookedHours);
