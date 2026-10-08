namespace SmartDesk.Domain.Enums;

// Where a reported problem stands (ADR-0037). Open and InProgress can go back and forth;
// Resolved is final, and the next problem of the kind is a new report.
public enum IssueStatus
{
    Open = 0,
    InProgress = 1,
    Resolved = 2,
}
