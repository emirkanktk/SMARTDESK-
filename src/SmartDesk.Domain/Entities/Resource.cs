using SmartDesk.Domain.Enums;

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

    public ResourceType ResourceType { get; set; } = null!;
    public ICollection<ResourceFeature> Features { get; set; } = new List<ResourceFeature>();
    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
    public ICollection<ResourceBlock> Blocks { get; set; } = new List<ResourceBlock>();
}
