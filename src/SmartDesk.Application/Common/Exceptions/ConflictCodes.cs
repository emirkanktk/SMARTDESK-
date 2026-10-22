namespace SmartDesk.Application.Common.Exceptions;

// The `code` of a 409 a client may need to act on (ADR-0023). Part of the API contract: renaming
// one breaks the screens that check it.
public static class ConflictCodes
{
    // The resource is Inactive or in Maintenance.
    public const string ResourceUnavailable = "resource-unavailable";

    // A blocked window covers part of the requested time.
    public const string ResourceBlocked = "resource-blocked";

    // Another reservation covers part of the requested time: Confirmed, or waiting for approval.
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

    // Its owner already has as many bookings of the kind that haven't ended as its rules allow
    // (ADR-0046): one has to go, or end, first.
    public const string BookingLimit = "booking-limit";

    // A date of a series outside office hours, for a kind booked within them only (ADR-0046):
    // skipped like a date that isn't free. Only ever a date's code, never a response's.
    public const string OutsideOfficeHours = "outside-office-hours";

    // Approving or declining a booking that isn't waiting for approval (ADR-0049): approved or
    // declined already, cancelled, or never asked for one.
    public const string NotAwaitingApproval = "not-awaiting-approval";

    // Approving a request whose start has come: it lapses instead.
    public const string ApprovalLapsed = "approval-lapsed";

    // Something a booking that waits for an Admin's approval can't have: a series, a place on
    // the waiting list, a booking at the door. Ask for one date, in the app.
    public const string ApprovalRequired = "approval-required";

    // Running longer, or ending, a booking still waiting for approval: it isn't one yet.
    public const string AwaitingApproval = "awaiting-approval";

    // Starring one more resource with FavoriteRules.MaxPerUser starred already (ADR-0051).
    public const string FavoritesFull = "favorites-full";

    // Adding a passkey with PasskeyRules.MaxPerUser on the account already (ADR-0057).
    public const string PasskeysFull = "passkeys-full";
}
