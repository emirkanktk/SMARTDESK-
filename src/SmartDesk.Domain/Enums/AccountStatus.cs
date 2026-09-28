namespace SmartDesk.Domain.Enums;

// Where an account stands (ADR-0020). Active is 0 so accounts that existed before statuses
// were introduced stay usable.
public enum AccountStatus
{
    Active = 0,

    // Signed up as a Manager; can't sign in until an Admin approves.
    PendingApproval = 1,

    // Switched off by an Admin, or by their Manager; can't sign in, and existing tokens stop working.
    Disabled = 2,
}
