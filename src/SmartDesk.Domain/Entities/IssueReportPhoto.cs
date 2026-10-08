namespace SmartDesk.Domain.Entities;

// The photo of a reported problem, at most one per report. Kept in the database like a
// resource's photo (ADR-0027), in a table of its own so lists of reports never load it.
public class IssueReportPhoto
{
    public Guid IssueReportId { get; set; }

    // Recognised from the image's first bytes when it was uploaded, not taken from the upload.
    public string ContentType { get; set; } = string.Empty;

    public byte[] Data { get; set; } = [];
}
