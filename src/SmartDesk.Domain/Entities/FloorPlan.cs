namespace SmartDesk.Domain.Entities;

// The drawing of one floor (ADR-0041): an image an Admin uploads, for the resources that share
// a location. Kept in the database like a resource's photo (ADR-0027).
public class FloorPlan
{
    // The resources' Location it belongs to, as they spell it: "5th Floor".
    public string Location { get; set; } = string.Empty;

    // Recognised from the image's first bytes when it was uploaded, not taken from the upload.
    public string ContentType { get; set; } = string.Empty;

    public byte[] Data { get; set; } = [];

    // In pixels, read from its header, so a client can lay the floor out before the image loads.
    public int Width { get; set; }
    public int Height { get; set; }

    // When it was last set; clients use it to tell a new plan from a cached one.
    public DateTime UpdatedAt { get; set; }
}
