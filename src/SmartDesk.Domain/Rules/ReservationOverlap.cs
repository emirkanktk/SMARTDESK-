using System.Linq.Expressions;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Rules;

// The one definition of "this time range is taken" (docs/DATA_MODEL.md, business rule 1),
// shared by the availability search and reservation creation so the two can never disagree.
//
// Ranges are half-open, [start, end): a booking that ends at 11:00 and one that starts at
// 11:00 do not overlap, so back-to-back meetings are allowed. Cancelled reservations never
// block anything.
public static class ReservationOverlap
{
    public static Expression<Func<Reservation, bool>> ConfirmedOverlapping(DateTime start, DateTime end) =>
        r => r.Status == ReservationStatus.Confirmed && r.StartDateTime < end && start < r.EndDateTime;

    public static bool Overlaps(DateTime startA, DateTime endA, DateTime startB, DateTime endB) =>
        startA < endB && startB < endA;
}
