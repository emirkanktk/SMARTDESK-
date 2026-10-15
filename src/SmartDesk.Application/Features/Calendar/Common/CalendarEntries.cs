using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Calendar.Common;

// A booking as an event in the calendar of one of its people, its owner or someone invited to
// it (ADR-0048), in the request's language. Titled with the resource, placed at its location,
// and described: who invited the reader and what they answered, who else is invited, the note,
// whether it has to be checked in, and a link back to it in SmartDesk.
public static class CalendarEntries
{
    public static CalendarEntry For(ReservationDto reservation, Guid readerId, IFrontendLinks links, TimeSpan checkInGrace)
    {
        var url = links.Reservation(reservation.Id);
        var answer = reservation.UserId == readerId
            ? null
            : reservation.Attendees.FirstOrDefault(a => a.Id == readerId)?.Response;

        var paragraphs = new List<string>();
        if (answer is { } response)
        {
            var said = Said(response);
            paragraphs.Add(reservation.UserDisplayName is { } organizer
                ? $"{string.Format(Messages.Calendar_InvitedBy, organizer)} {said}"
                : said);
        }

        var invited = reservation.Attendees.Select(a => a.Name).OfType<string>().ToList();
        if (invited.Count > 0)
        {
            paragraphs.Add(string.Format(Messages.Calendar_Invited, string.Join(", ", invited)));
        }

        if (!string.IsNullOrWhiteSpace(reservation.Note))
        {
            paragraphs.Add(reservation.Note.Trim());
        }

        if (reservation.CheckInRequired)
        {
            paragraphs.Add(string.Format(Messages.Calendar_CheckIn, Durations.Describe(checkInGrace)));
        }

        paragraphs.Add(string.Format(Messages.Calendar_Open, url));

        return new CalendarEntry(
            Uid(reservation.Id),
            reservation.StartDateTime,
            reservation.EndDateTime,
            reservation.ResourceName,
            reservation.ResourceLocation,
            string.Join("\n\n", paragraphs),
            url,
            Status(reservation.Status, answer));
    }

    // The same for a booking wherever it's written, so a calendar app keeps one copy of it.
    public static string Uid(Guid reservationId) => $"{reservationId}@smartdesk";

    private static CalendarEntryStatus Status(ReservationStatus status, AttendeeResponse? answer) =>
        (status, answer) switch
        {
            (ReservationStatus.Cancelled, _) => CalendarEntryStatus.Cancelled,
            (_, AttendeeResponse.Pending) => CalendarEntryStatus.Tentative,
            (_, AttendeeResponse.Declined) => CalendarEntryStatus.Cancelled,
            _ => CalendarEntryStatus.Confirmed,
        };

    private static string Said(AttendeeResponse response) => response switch
    {
        AttendeeResponse.Accepted => Messages.Calendar_Coming,
        AttendeeResponse.Declined => Messages.Calendar_NotComing,
        _ => Messages.Calendar_NotAnswered,
    };
}
