namespace SmartDesk.Application.Features.Resources.Common;

// The editable fields shared by CreateResourceCommand and UpdateResourceCommand, so both
// are validated by the same rules (ResourceFieldsValidator).
public interface IResourceFields
{
    string Name { get; }
    Guid ResourceTypeId { get; }
    int Capacity { get; }
    string Location { get; }
    string? Description { get; }
    IReadOnlyList<string>? Features { get; }
}
