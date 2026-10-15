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

    // When its owner was asked whether they need longer (ADR-0033). Cleared when the end
    // moves, so a meeting that runs on is asked again before its new end.
    public DateTime? EndingSoonSentAt { get; set; }

    // Who checked it in: the owner, or someone invited to it (ADR-0034). Cleared with
    // CheckedInAt.
    public Guid? CheckedInByUserId { get; set; }

    // When the owner and those invited were warned that nobody has checked in yet and it's
    // about to be released (ADR-0034). That's after its start, when neither the start nor the
    // resource can change any more, so it's never cleared.
    public DateTime? ReleaseWarningSentAt { get; set; }

    // Whether its resource asked for a check-in when it was booked or last moved (ADR-0030).
    // It's released unchecked only while the resource still asks (CheckInRules.Needed).
    public bool CheckInRequired { get; set; }

    // When the owner checked in at the door. Cleared when the start or the resource changes:
    // that's another visit.
    public DateTime? CheckedInAt { get; set; }

    // When it was released because nobody checked in; its Status is Cancelled from then on.
    public DateTime? ReleasedAt { get; set; }

    // Its kind waits for an Admin's approval (ADR-0049): when it last went to one, on being
    // asked for or changed; null for a booking that never needed it.
    public DateTime? RequestedAt { get; set; }

    // When an Admin approved or declined it, or it lapsed at its start still waiting; cleared
    // when it goes to be approved again.
    public DateTime? DecidedAt { get; set; }

    // The Admin who decided; null while it waits, and when it lapsed.
    public Guid? DecidedByUserId { get; set; }

    // Why it was declined, for its owner.
    public string? DeclineReason { get; set; }

    // The repeating booking it's one date of (ADR-0035); null for a booking made on its own.
    public Guid? SeriesId { get; set; }

    public ReservationSeries? Series { get; set; }

    public Resource Resource { get; set; } = null!;

    // The people the organizer invited to it (ADR-0032); usually none.
    public ICollection<ReservationAttendee> Attendees { get; set; } = new List<ReservationAttendee>();
}
