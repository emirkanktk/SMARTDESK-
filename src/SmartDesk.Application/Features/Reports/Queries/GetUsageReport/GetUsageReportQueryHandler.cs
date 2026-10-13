using MediatR;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Extensions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Features.Reports.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Reports.Queries.GetUsageReport;

// ADR-0017. Everything is computed in memory from the reservations overlapping the range:
// at most 92 days, so the rows stay few, and clipping to working hours per local day is far
// clearer in C# than in SQL. So are the heat map's local hours and the no-shows (ADR-0044).
public sealed class GetUsageReportQueryHandler(IApplicationDbContext db, IIdentityService identityService)
    : IRequestHandler<GetUsageReportQuery, UsageReportDto>
{
    public async Task<UsageReportDto> Handle(GetUsageReportQuery request, CancellationToken cancellationToken)
    {
        var zone = string.IsNullOrWhiteSpace(request.TimeZone)
            ? TimeZoneInfo.Utc
            : TimeZoneInfo.FindSystemTimeZoneById(request.TimeZone);

        // [fromUtc, toUtc): local midnight starting From to local midnight after To. Days can be
        // 23 or 25 hours long, so both ends are converted rather than derived from each other.
        var fromUtc = zone.LocalToUtc(request.From.ToDateTime(TimeOnly.MinValue));
        var toUtc = zone.LocalToUtc(request.To.AddDays(1).ToDateTime(TimeOnly.MinValue));

        // Utilization is booked time within the office's hours (ADR-0046), read in the zone the
        // report is for, as its days are. A desk booked overnight or a car over the weekend still
        // counts in BookedHours, but not against the office day.
        var office = OfficeWeek.Of(await db.OfficeHours.AsNoTracking().SingleAsync(cancellationToken), zone);
        var workingWindows = office.OpenBetween(fromUtc, toUtc);
        var hoursPerResource = workingWindows.Sum(w => (w.End - w.Start).TotalHours);

        var reservationsQuery = db.Reservations.AsNoTracking()
            .Where(r => r.StartDateTime < toUtc && fromUtc < r.EndDateTime);
        var resourcesQuery = db.Resources.AsNoTracking();
        if (request.ResourceTypeId is { } typeId)
        {
            reservationsQuery = reservationsQuery.Where(r => r.Resource.ResourceTypeId == typeId);
            resourcesQuery = resourcesQuery.Where(r => r.ResourceTypeId == typeId);
        }

        var bookings = await reservationsQuery
            .Select(r => new Booking(
                r.ResourceId, r.UserId, r.StartDateTime, r.EndDateTime, r.Status, r.CheckedInAt != null, r.ReleasedAt != null))
            .ToListAsync(cancellationToken);
        var resources = await resourcesQuery
            .Select(r => new { r.Id, r.Name, r.ResourceTypeId, TypeName = r.ResourceType.Name, r.Status })
            .ToListAsync(cancellationToken);

        // Active resources, plus any other that had reservations in the range: a room retired
        // last week still shows what it was used for, but long-retired ones don't dilute the
        // totals with empty rows.
        var byResource = bookings.ToLookup(b => b.ResourceId);
        var rows = resources
            .Where(r => r.Status == ResourceStatus.Active || byResource.Contains(r.Id))
            .Select(r =>
            {
                var confirmed = byResource[r.Id].Where(b => b.Status == ReservationStatus.Confirmed).ToList();
                return new Row(
                    r.Id, r.Name, r.ResourceTypeId, r.TypeName, r.Status,
                    Reservations: confirmed.Count,
                    Cancelled: byResource[r.Id].Count(b => b.Status == ReservationStatus.Cancelled),
                    BookedHours: confirmed.Sum(b => Overlap(b.Start, b.End, fromUtc, toUtc)),
                    WorkingHoursBooked: confirmed.Sum(b => WorkingOverlap(b.Start, b.End, workingWindows)),
                    NoShows: byResource[r.Id].Count(b => b.Status == ReservationStatus.Cancelled && b.Released),
                    CheckedIn: confirmed.Count(b => b.CheckedIn));
            })
            .ToList();

        var heatMap = WeekHeatMap.Compute(
            zone,
            request.From,
            request.To,
            bookings.Where(b => b.Status == ReservationStatus.Confirmed).Select(b => (b.Start, b.End)),
            rows.Count);
        var noShows = rows.Sum(r => r.NoShows);
        var checkedIn = rows.Sum(r => r.CheckedIn);
        var cancelled = rows.Sum(r => r.Cancelled);

        var topUsers = await TopUsersAsync(bookings, fromUtc, toUtc, cancellationToken);

        return new UsageReportDto(
            request.From,
            request.To,
            zone.Id,
            fromUtc,
            toUtc,
            office.Describe(),
            office.Opens,
            office.Closes,
            Round(hoursPerResource),
            new UsageTotals(
                rows.Count,
                rows.Sum(r => r.Reservations),
                cancelled,
                Round(rows.Sum(r => r.BookedHours)),
                Round(rows.Sum(r => r.WorkingHoursBooked)),
                Round(hoursPerResource * rows.Count),
                Ratio(rows.Sum(r => r.WorkingHoursBooked), hoursPerResource * rows.Count),
                noShows,
                checkedIn,
                Ratio(noShows, noShows + checkedIn),
                // Cancelled by someone: a release isn't a choice anybody made.
                Ratio(cancelled - noShows, rows.Sum(r => r.Reservations) + cancelled)),
            rows
                .Select(r => new ResourceUsage(
                    r.Id, r.Name, r.TypeId, r.TypeName, r.Status, r.Reservations, r.Cancelled,
                    Round(r.BookedHours), Round(r.WorkingHoursBooked), Ratio(r.WorkingHoursBooked, hoursPerResource),
                    r.NoShows, r.CheckedIn, Ratio(r.NoShows, r.NoShows + r.CheckedIn)))
                .OrderByDescending(r => r.Utilization)
                .ThenByDescending(r => r.BookedHours)
                .ThenBy(r => r.ResourceName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            rows
                .GroupBy(r => (r.TypeId, r.TypeName))
                .Select(g => new ResourceTypeUsage(
                    g.Key.TypeId, g.Key.TypeName, g.Count(), g.Sum(r => r.Reservations), g.Sum(r => r.Cancelled),
                    Round(g.Sum(r => r.BookedHours)), Round(g.Sum(r => r.WorkingHoursBooked)),
                    Round(hoursPerResource * g.Count()), Ratio(g.Sum(r => r.WorkingHoursBooked), hoursPerResource * g.Count()),
                    g.Sum(r => r.NoShows), g.Sum(r => r.CheckedIn), Ratio(g.Sum(r => r.NoShows), g.Sum(r => r.NoShows + r.CheckedIn))))
                .OrderByDescending(t => t.Utilization)
                .ThenBy(t => t.ResourceTypeName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            topUsers,
            heatMap);
    }

    private async Task<IReadOnlyList<UserUsage>> TopUsersAsync(
        List<Booking> bookings, DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        var top = bookings
            .Where(b => b.Status == ReservationStatus.Confirmed)
            .GroupBy(b => b.UserId)
            .Select(g => (UserId: g.Key, Count: g.Count(), Hours: g.Sum(b => Overlap(b.Start, b.End, fromUtc, toUtc))))
            .OrderByDescending(u => u.Hours)
            .ThenByDescending(u => u.Count)
            .ThenBy(u => u.UserId)
            .Take(ReportRules.TopUsers)
            .ToList();

        var names = await identityService.GetDisplayNamesAsync(top.Select(u => u.UserId).ToList(), cancellationToken);

        return top
            .Select(u => new UserUsage(u.UserId, names.GetValueOrDefault(u.UserId), u.Count, Round(u.Hours)))
            .ToList();
    }

    private static double WorkingOverlap(DateTime start, DateTime end, IReadOnlyList<(DateTime Start, DateTime End)> windows)
    {
        var hours = 0.0;
        foreach (var window in windows)
        {
            if (window.Start >= end)
            {
                break;
            }

            hours += Overlap(start, end, window.Start, window.End);
        }

        return hours;
    }

    // Hours shared by [aStart, aEnd) and [bStart, bEnd); 0 when they don't overlap.
    private static double Overlap(DateTime aStart, DateTime aEnd, DateTime bStart, DateTime bEnd)
    {
        var start = aStart > bStart ? aStart : bStart;
        var end = aEnd < bEnd ? aEnd : bEnd;
        return end > start ? (end - start).TotalHours : 0;
    }

    private static double Round(double hours) => Math.Round(hours, 2, MidpointRounding.AwayFromZero);

    private static double Ratio(double part, double whole) =>
        whole > 0 ? Math.Round(part / whole, 4, MidpointRounding.AwayFromZero) : 0;

    private sealed record Booking(
        Guid ResourceId, Guid UserId, DateTime Start, DateTime End, ReservationStatus Status, bool CheckedIn, bool Released);

    private sealed record Row(
        Guid Id, string Name, Guid TypeId, string TypeName, ResourceStatus Status,
        int Reservations, int Cancelled, double BookedHours, double WorkingHoursBooked, int NoShows, int CheckedIn);
}
