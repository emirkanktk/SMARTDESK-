namespace SmartDesk.Domain.Entities;

// A resource someone starred, to find and book it quickly (ADR-0051). Theirs alone, and kept
// with their account rather than in a browser, so it's the same on every device. One row per
// account and resource.
public class FavoriteResource
{
    // The account it's for, with no foreign key across the Identity boundary, like
    // Reservation.UserId.
    public Guid UserId { get; set; }

    public Guid ResourceId { get; set; }

    public DateTime CreatedAt { get; set; }

    public Resource Resource { get; set; } = null!;
}
