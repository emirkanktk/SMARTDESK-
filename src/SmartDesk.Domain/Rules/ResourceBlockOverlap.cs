using System.Linq.Expressions;
using SmartDesk.Domain.Entities;

namespace SmartDesk.Domain.Rules;

// Same half-open [start, end) rule as ReservationOverlap: a block ending at 12:00 doesn't
// stop a booking that starts at 12:00.
public static class ResourceBlockOverlap
{
    public static Expression<Func<ResourceBlock, bool>> Overlapping(DateTime start, DateTime end) =>
        b => b.StartDateTime < end && start < b.EndDateTime;
}
