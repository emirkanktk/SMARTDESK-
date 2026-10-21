using System.Globalization;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Features.Push.Common;

// A notification in words: what the device shows, where tapping it goes, and the tag that makes a
// newer notification about the same thing replace the older one.
public sealed record PushContent(string Title, string Body, string Url, string Tag);

// A notice in words for one person (ADR-0056): in their language, with the times in the office's
// zone, since the device shows what it's given and the server keeps nobody's own zone. Messages say
// what the inbox says (ADR-0032), so the lock screen and the app read alike.
public static class PushTexts
{
    // Reasons and notes are cut to this: a notification is read at a glance, and a push message has
    // a few kilobytes at most.
    public const int MaxDetailLength = 120;

    public const string BookingsUrl = "/reservations/mine";
    public const string InboxUrl = "/inbox";
    public const string ApprovalsUrl = "/admin/approvals";
    public const string ProfileUrl = "/profile";

    public static PushContent Compose(PushNotice notice, CultureInfo culture, TimeZoneInfo zone, TimeSpan checkInGrace) =>
        notice.Topic switch
        {
            PushTopic.InboxMessage => ForMessage(
                notice.Message ?? throw new ArgumentException("An inbox notice carries its message.", nameof(notice)),
                culture,
                zone),
            PushTopic.Test => new PushContent(
                T(culture, nameof(Messages.Push_Test_Title)), T(culture, nameof(Messages.Push_Test_Body)), ProfileUrl, "test"),
            _ => ForBooking(
                notice.Topic,
                notice.Reservation ?? throw new ArgumentException("A booking notice carries its booking.", nameof(notice)),
                culture,
                zone,
                checkInGrace),
        };

    private static PushContent ForBooking(
        PushTopic topic, ReservationDto booking, CultureInfo culture, TimeZoneInfo zone, TimeSpan checkInGrace)
    {
        var when = When(booking.StartDateTime, booking.EndDateTime, culture, zone);
        var (title, body) = topic switch
        {
            PushTopic.StartingSoon => (nameof(Messages.Push_StartingSoon_Title), Which(booking.ResourceName, when)),
            PushTopic.ReleaseWarning => (
                nameof(Messages.Push_ReleaseWarning_Title),
                T(culture, nameof(Messages.Push_ReleaseWarning_Body),
                    booking.ResourceName, when, Clock(booking.StartDateTime + checkInGrace, zone))),
            PushTopic.Released => (
                nameof(Messages.Push_Released_Title),
                T(culture, nameof(Messages.Push_Released_Body), booking.ResourceName, when)),
            PushTopic.EndingSoon => (
                nameof(Messages.Push_EndingSoon_Title),
                T(culture, nameof(Messages.Push_EndingSoon_Body), booking.ResourceName, Clock(booking.EndDateTime, zone))),
            _ => throw new ArgumentOutOfRangeException(nameof(topic), topic, "Not a notice about a booking."),
        };

        // One tag per booking and notice: a warning doesn't silently take the reminder's place.
        return new PushContent(T(culture, title), body, BookingsUrl, $"{topic}:{booking.Id}");
    }

    private static PushContent ForMessage(InboxMessageDto message, CultureInfo culture, TimeZoneInfo zone)
    {
        var sender = message.SenderName ?? T(culture, nameof(Messages.Push_Someone));
        var title = message.Kind switch
        {
            InboxMessageKind.MeetingInvitation => T(culture, nameof(Messages.Push_Inbox_MeetingInvitation), sender),
            InboxMessageKind.MeetingChanged => T(culture, nameof(Messages.Push_Inbox_MeetingChanged), sender),
            // A release has nobody behind it (ADR-0030).
            InboxMessageKind.MeetingCancelled when message.SenderId is null => T(culture, nameof(Messages.Push_Inbox_MeetingReleased)),
            InboxMessageKind.MeetingCancelled => T(culture, nameof(Messages.Push_Inbox_MeetingCancelled), sender),
            InboxMessageKind.MeetingUninvited => T(culture, nameof(Messages.Push_Inbox_MeetingUninvited), sender),
            InboxMessageKind.MeetingAccepted => T(culture, nameof(Messages.Push_Inbox_MeetingAccepted), sender),
            InboxMessageKind.MeetingDeclined => T(culture, nameof(Messages.Push_Inbox_MeetingDeclined), sender),
            InboxMessageKind.MeetingExtended => T(culture, nameof(Messages.Push_Inbox_MeetingExtended), sender),
            InboxMessageKind.SeriesInvitation => T(culture, nameof(Messages.Push_Inbox_SeriesInvitation), sender),
            InboxMessageKind.SeriesCancelled => T(culture, nameof(Messages.Push_Inbox_SeriesCancelled), sender),
            InboxMessageKind.SeriesAccepted => T(culture, nameof(Messages.Push_Inbox_SeriesAccepted), sender),
            InboxMessageKind.SeriesDeclined => T(culture, nameof(Messages.Push_Inbox_SeriesDeclined), sender),
            InboxMessageKind.IssueResolved => T(culture, nameof(Messages.Push_Inbox_IssueResolved)),
            InboxMessageKind.CancelledByBlock => T(culture, nameof(Messages.Push_Inbox_CancelledByBlock), sender),
            InboxMessageKind.WaitlistBooked => T(culture, nameof(Messages.Push_Inbox_WaitlistBooked)),
            InboxMessageKind.ApprovalRequested => T(culture, nameof(Messages.Push_Inbox_ApprovalRequested), sender),
            InboxMessageKind.BookingApproved => T(culture, nameof(Messages.Push_Inbox_BookingApproved), sender),
            InboxMessageKind.BookingDeclined => T(culture, nameof(Messages.Push_Inbox_BookingDeclined), sender),
            InboxMessageKind.ApprovalLapsed => T(culture, nameof(Messages.Push_Inbox_ApprovalLapsed)),
            _ => T(culture, nameof(Messages.Push_Inbox_New)),
        };

        return new PushContent(
            title,
            MessageBody(message, culture, zone),
            message.Kind == InboxMessageKind.ApprovalRequested ? ApprovalsUrl : InboxUrl,
            $"inbox-{message.Id}");
    }

    private static string MessageBody(InboxMessageDto message, CultureInfo culture, TimeZoneInfo zone)
    {
        // A fixed problem's times are when it was reported and resolved; what matters is what it was.
        if (message.Kind == InboxMessageKind.IssueResolved)
        {
            return string.IsNullOrWhiteSpace(message.Note)
                ? message.ResourceName
                : $"{message.ResourceName}: {Cut(message.Note)}";
        }

        // A meeting that repeats is told from the first date the message covers (ADR-0035).
        var when = When(message.StartDateTime, message.EndDateTime, culture, zone);
        if (message.Series is not null)
        {
            when = T(culture, nameof(Messages.Push_SeriesWhen), when);
        }

        // Why someone can't come, why it was declined, why the resource was closed.
        var reason = message.Kind is InboxMessageKind.MeetingDeclined or InboxMessageKind.SeriesDeclined
            or InboxMessageKind.BookingDeclined or InboxMessageKind.CancelledByBlock
            ? message.Reason
            : null;

        return string.IsNullOrWhiteSpace(reason)
            ? Which(message.ResourceName, when)
            : $"{Which(message.ResourceName, when)}. {Cut(reason)}";
    }

    // "Board Room, 21 Eki Çar 14:00–15:00": the same in both languages, so not a message of its own.
    private static string Which(string resource, string when) => $"{resource}, {when}";

    // "21 Eki Sal 14:00–15:00", or both days when it runs into another.
    private static string When(DateTime startUtc, DateTime endUtc, CultureInfo culture, TimeZoneInfo zone)
    {
        var start = Local(startUtc, zone);
        var end = Local(endUtc, zone);
        var day = start.ToString("d MMM ddd", culture);
        return start.Date == end.Date
            ? $"{day} {Clock(start)}–{Clock(end)}"
            : $"{day} {Clock(start)} – {end.ToString("d MMM ddd", culture)} {Clock(end)}";
    }

    private static DateTime Local(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    private static string Clock(DateTime local) => local.ToString("HH:mm", CultureInfo.InvariantCulture);

    private static string Clock(DateTime utc, TimeZoneInfo zone) => Clock(Local(utc, zone));

    private static string Cut(string text) =>
        text.Length <= MaxDetailLength ? text : string.Concat(text.AsSpan(0, MaxDetailLength - 1), "…");

    private static string T(CultureInfo culture, string key, params object[] args)
    {
        var template = Messages.ResourceManager.GetString(key, culture)
            ?? throw new InvalidOperationException($"No message '{key}'.");
        return args.Length == 0 ? template : string.Format(culture, template, args);
    }
}
