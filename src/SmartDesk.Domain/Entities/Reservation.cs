using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Entities;

public class Reservation
{
    public Guid Id { get; set; }
    public Guid ResourceId { get; set; }
    public Guid UserId { get; set; }
    public DateTime StartDateTime { get; set; }
    public DateTime EndDateTime { get; set; }
    public ReservationStatus Status { get; set; } = ReservationStatus.Confirmed;
    public string? Note { get; set; }

    public Resource Resource { get; set; } = null!;
}
