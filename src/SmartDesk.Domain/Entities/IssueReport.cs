using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Entities;

// A problem someone reported at a resource, usually from the QR code at its door (ADR-0037):
// the TV doesn't work, the room needs cleaning. Whoever may manage the resource follows it up.
public class IssueReport
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }

    // An account id, with no foreign key across the Identity boundary.
    public Guid ReportedByUserId { get; set; }

    public IssueCategory Category { get; set; }
    public string Description { get; set; } = string.Empty;

    // The resource can't be used until it's fixed. Such reports come first in the list.
    public bool IsBlocking { get; set; }

    public IssueStatus Status { get; set; } = IssueStatus.Open;
    public DateTime CreatedAt { get; set; }

    // Set when it becomes Resolved, which is final.
    public DateTime? ResolvedAt { get; set; }

    public Resource Resource { get; set; } = null!;

    // Optional: a picture of the problem, taken with the phone at the door.
    public IssueReportPhoto? Photo { get; set; }
}
