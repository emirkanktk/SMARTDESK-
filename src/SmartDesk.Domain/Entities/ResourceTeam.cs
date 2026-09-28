namespace SmartDesk.Domain.Entities;

// A team a resource belongs to (ADR-0022). A team is identified by its Manager's account id; the
// account itself lives in Identity, so this is a plain id with no foreign key across that
// boundary, like Reservation.UserId. A resource with no teams is shared by everyone; one with
// several is shared by those teams, first come first served.
public class ResourceTeam
{
    public Guid ResourceId { get; set; }
    public Guid TeamId { get; set; }

    public Resource Resource { get; set; } = null!;
}
