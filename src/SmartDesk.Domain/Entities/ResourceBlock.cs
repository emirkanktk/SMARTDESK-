namespace SmartDesk.Domain.Entities;

// A window in which a resource can't be booked - planned maintenance, a room reserved for an
// event, a car at the service. Unlike ResourceStatus.Maintenance, which takes a resource out
// indefinitely, a block covers one [StartDateTime, EndDateTime) range.
public class ResourceBlock
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public string Reason { get; set; } = string.Empty;
    public Guid CreatedByUserId { get; set; }

    public Resource Resource { get; set; } = null!;
}
