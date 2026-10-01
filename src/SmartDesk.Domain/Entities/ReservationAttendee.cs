using SmartDesk.Domain.Enums;

namespace SmartDesk.Domain.Entities;

// Someone the organizer invited to a booking (ADR-0032): a manager meeting their team in a
// room. UserId is an account id, with no foreign key across the Identity boundary, like
// Reservation.UserId.
public class ReservationAttendee
{
    public Guid ReservationId { get; set; }
    public Guid UserId { get; set; }

    // Their answer, back to Pending when the meeting moves to a new start or another room.
    // The reason for not coming is for the organizer only: it's in their inbox, never in the
    // booking that everyone's calendar shows.
    public AttendeeResponse Response { get; set; }
    public DateTime? RespondedAt { get; set; }
    public string? DeclineReason { get; set; }

    public Reservation Reservation { get; set; } = null!;
}
