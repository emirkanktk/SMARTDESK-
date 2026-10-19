namespace SmartDesk.Application.Features.Audit.Common;

// One column of a row, before and after a change (ADR-0052), every value as text: instants in
// UTC ISO 8601, enums by name, a secret as "***". Old is null for a new row, New for a deleted one.
public sealed record AuditChange(string Property, string? Old, string? New);
