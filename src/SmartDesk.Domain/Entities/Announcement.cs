using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Entities;

// News for the whole office from its Admins (ADR-0065): shown on everyone's dashboard and on the
// lobby screens (ADR-0064) from the moment it's posted until EndsAt, and sent to the phones and
// browsers with notifications on if its Admin asks.
public class Announcement
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    // A few lines more, or none.
    public string? Body { get; set; }

    public AnnouncementTone Tone { get; set; }

    // When it was posted, and so when it starts showing.
    public DateTime CreatedAt { get; set; }

    public DateTime EndsAt { get; set; }

    // The Admin who posted it, with no foreign key across the Identity boundary; null once that
    // account is gone.
    public Guid? CreatedByUserId { get; set; }
}
