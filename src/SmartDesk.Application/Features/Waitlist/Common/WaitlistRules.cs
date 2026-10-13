namespace SmartDesk.Application.Features.Waitlist.Common;

// The waiting list's limits (ADR-0043).
public static class WaitlistRules
{
    // How many times someone may wait for at once: enough for a busy week, not a way to queue
    // for every room in the building.
    public const int MaxPerUser = 10;

    // A time that frees up once it has begun, with less than this of it left, isn't booked for
    // anyone: the entry waiting for it goes instead.
    public static readonly TimeSpan MinimumLeft = TimeSpan.FromMinutes(15);

    // The reminder a booking from the waiting list gets, as the booking form suggests one, when
    // it starts later than that.
    public const int ReminderMinutes = 15;
}
