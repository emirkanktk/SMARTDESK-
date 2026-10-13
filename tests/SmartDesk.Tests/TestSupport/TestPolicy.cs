using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.BookingRules.Common;

namespace SmartDesk.Tests.TestSupport;

// The booking policy (ADR-0046) for handler tests, on the test's clock. With the rules as the
// migration seeds them - no kind has rules of its own - a booking keeps only to the rules for
// every booking.
public static class TestPolicy
{
    public static BookingPolicy For(IApplicationDbContext db, TimeProvider? clock = null) => new(db, clock ?? TimeProvider.System);
}
