namespace SmartDesk.Domain.Enums;

public enum ReservationStatus
{
    Confirmed = 0,
    Cancelled = 1,

    // Waiting for an Admin's approval (ADR-0049). It holds its time as a booking does, so nobody
    // else can take it meanwhile, but it isn't one yet: no reminder, no check-in, not in the
    // reports. Approved, it's Confirmed; declined, or still waiting at its start, Cancelled.
    Pending = 2,
}
