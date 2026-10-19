namespace SmartDesk.Domain.Enums;

// Who a change came from (ADR-0052).
public enum AuditActor
{
    // A signed-in person, named by AuditEntry.UserId.
    User = 0,

    // A request without an account: the page a QR code opens (ADR-0050), or signing up.
    Anonymous = 1,

    // SmartDesk itself, in a background job: a release for want of a check-in, a request that
    // lapsed, a waiting list served.
    System = 2,
}
