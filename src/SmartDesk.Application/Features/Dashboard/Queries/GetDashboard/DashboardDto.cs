using SmartDesk.Application.Features.Reservations.Common;

namespace SmartDesk.Application.Features.Dashboard.Queries.GetDashboard;

public sealed record DashboardDto(
    DateTime GeneratedAt,
    string TimeZone,
    // The UTC instants "today" was taken to span in TimeZone, so the screen and the server
    // agree on which day is shown.
    DateTime TodayStart,
    DateTime TodayEnd,
    DashboardStats Stats,
    // Every Confirmed reservation overlapping today, across all resources, earliest first.
    IReadOnlyList<ReservationDto> Today,
    // Confirmed reservations running right now - i.e. the resources currently in use.
    IReadOnlyList<ReservationDto> InUseNow,
    // The caller's next Confirmed reservations that haven't started yet.
    IReadOnlyList<ReservationDto> MyUpcoming);

public sealed record DashboardStats(
    int ActiveResources,
    int ResourcesInUseNow,
    int ReservationsToday,
    // The full count; MyUpcoming itself is capped.
    int MyUpcomingReservations);
