using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Domain.Entities;

public class Resource
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public Guid ResourceTypeId { get; set; }
    public int Capacity { get; set; }
    public string Location { get; set; } = string.Empty;
    public ResourceStatus Status { get; set; } = ResourceStatus.Active;
    public string? Description { get; set; }

    // Bookings of it must be checked in at the door, or they're released (ADR-0030).
    public bool CheckInRequired { get; set; }

    // The secret in its QR code, so a check-in comes from someone who has been at the door.
    // Only those who may edit the resource see it; replacing it retires the printed code.
    public string CheckInCode { get; set; } = CheckInRules.NewCode();

    public ResourceType ResourceType { get; set; } = null!;
    public ICollection<ResourceFeature> Features { get; set; } = new List<ResourceFeature>();
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public ICollection<ResourceBlock> Blocks { get; set; } = new List<ResourceBlock>();

    // The teams that may use it; none means everyone may (ADR-0022).
    public ICollection<ResourceTeam> Teams { get; set; } = new List<ResourceTeam>();

    // Optional; its type's picture is shown when there's none.
    public ResourcePhoto? Photo { get; set; }
}
