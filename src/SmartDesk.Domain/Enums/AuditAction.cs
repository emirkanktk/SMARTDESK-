namespace SmartDesk.Domain.Enums;

// What a save did to a row (ADR-0052).
public enum AuditAction
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
}
