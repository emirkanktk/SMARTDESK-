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

    // Minutes before the start to remind the owner; null means no reminder (ADR-0018).
    public int? ReminderMinutes { get; set; }

    // When the reminder went out, so it goes out once. Cleared when the start or the reminder
    // changes, so a moved reservation is reminded again.
    public DateTime? ReminderSentAt { get; set; }

    public Resource Resource { get; set; } = null!;
}
