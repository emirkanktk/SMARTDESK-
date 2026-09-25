using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reports.Queries.GetUsageReport;

// Hours are rounded to 2 decimals and utilization (booked working hours / available working
// hours, 0-1) to 4, after summing, so totals don't drift from the rows.
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
    IReadOnlyList<UserUsage> TopUsers);

public sealed record UsageTotals(
    int Resources,
    int Reservations,
    int Cancelled,
    double BookedHours,
    double WorkingHoursBooked,
    double WorkingHoursAvailable,
    double Utilization);

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
    double Utilization);

public sealed record ResourceTypeUsage(
    Guid ResourceTypeId,
    string ResourceTypeName,
    int Resources,
    int Reservations,
    int Cancelled,
    double BookedHours,
    double WorkingHoursBooked,
    double WorkingHoursAvailable,
    double Utilization);

public sealed record UserUsage(Guid UserId, string? DisplayName, int Reservations, double BookedHours);
