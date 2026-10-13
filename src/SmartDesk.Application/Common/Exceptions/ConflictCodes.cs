namespace SmartDesk.Application.Common.Exceptions;

// The `code` of a 409 a client may need to act on (ADR-0023). Part of the API contract: renaming
// one breaks the screens that check it.
public static class ConflictCodes
{
    // The resource is Inactive or in Maintenance.
    public const string ResourceUnavailable = "resource-unavailable";

    // A blocked window covers part of the requested time.
    public const string ResourceBlocked = "resource-blocked";

    // Another Confirmed reservation covers part of the requested time.
    public const string ReservationOverlaps = "reservation-overlaps";

    // A new blocked window would overlap another one on the same resource.
    public const string BlockOverlapsBlock = "block-overlaps-block";

    // A new blocked window would cover Confirmed reservations, which have to go first.
    public const string BlockOverlapsReservations = "block-overlaps-reservations";

    // A resource can't go into maintenance or be switched off while Confirmed reservations
    // on it are still to come (issue #41).
    public const string ResourceHasReservations = "resource-has-reservations";

    // A check-in at a resource that doesn't ask for one (ADR-0030).
    public const string CheckInNotUsed = "check-in-not-used";

    // A check-in, but none of the caller's bookings of that resource is open for one now.
    public const string NothingToCheckIn = "nothing-to-check-in";

    // Booking a series, some of its dates aren't free (ADR-0035): look at them again
    // (GET /api/reservations/series/preview), then book the others or choose another time.
    public const string SeriesConflicts = "series-conflicts";

    // The same problem is already reported on the resource and isn't fixed yet (ADR-0037).
    public const string IssueAlreadyReported = "issue-already-reported";

    // A report that's resolved can't be moved back: the problem coming back is a new report.
    public const string IssueResolved = "issue-resolved";

    // Waiting for a time that's free (ADR-0043): book it instead.
    public const string WaitlistSlotFree = "waitlist-slot-free";

    // Already waiting for that resource at part of that time.
    public const string WaitlistAlreadyWaiting = "waitlist-already-waiting";
}
