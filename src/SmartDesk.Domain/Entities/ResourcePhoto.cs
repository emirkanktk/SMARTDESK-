namespace SmartDesk.Domain.Entities;

// A photo of a resource, so people can see what they're booking. At most one per resource,
// kept in the database next to it: nothing to back up or serve separately.
public class ResourcePhoto
{
    public Guid ResourceId { get; set; }

    // Recognised from the image's first bytes when it was uploaded, not taken from the upload.
    public string ContentType { get; set; } = string.Empty;

    public byte[] Data { get; set; } = [];

    // When it was last set; clients use it to tell a new photo from a cached one.
    public DateTime UpdatedAt { get; set; }
}
