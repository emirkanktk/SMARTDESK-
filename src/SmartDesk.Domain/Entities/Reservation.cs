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

    // Whether its resource asked for a check-in when it was booked or last moved (ADR-0030).
    // It's released unchecked only while the resource still asks (CheckInRules.Needed).
    public bool CheckInRequired { get; set; }

    // When the owner checked in at the door. Cleared when the start or the resource changes:
    // that's another visit.
    public DateTime? CheckedInAt { get; set; }

    // When it was released because nobody checked in; its Status is Cancelled from then on.
    public DateTime? ReleasedAt { get; set; }

    public Resource Resource { get; set; } = null!;
}
