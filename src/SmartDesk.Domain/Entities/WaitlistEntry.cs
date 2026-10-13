namespace SmartDesk.Domain.Entities;

// Someone waiting for a resource at a time it's taken (ADR-0043). When that time frees up - the
// booking in it cancelled, released for want of a check-in, ended early or moved, or a block
// lifted - it's booked for whoever has waited longest, and the entry goes.
public class WaitlistEntry
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public Guid UserId { get; set; }

    // The time they want, as they'd book it. Once it has begun, what's left of it.
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }

    // When they joined: the line is in this order.
    public DateTime CreatedAt { get; set; }
}
