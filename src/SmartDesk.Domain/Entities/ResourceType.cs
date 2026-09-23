namespace SmartDesk.Domain.Entities;

public class ResourceType
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    public ICollection<Resource> Resources { get; set; } = new List<Resource>();
}
