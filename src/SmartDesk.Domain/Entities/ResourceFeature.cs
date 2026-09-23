namespace SmartDesk.Domain.Entities;

public class ResourceFeature
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public string Name { get; set; } = string.Empty;

    public Resource Resource { get; set; } = null!;
}
