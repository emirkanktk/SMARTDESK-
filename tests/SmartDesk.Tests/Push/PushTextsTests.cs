using System.Globalization;
using FluentAssertions;
using SmartDesk.Application.Features.Inbox.Common;
using SmartDesk.Application.Features.Push.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Tests.Push;

// What a notification says (ADR-0056): in its reader's language, with the times in the office's
// zone, the way the inbox says it, and where tapping it goes.
public sealed class PushTextsTests
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr");
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

    // 14:00-15:00 in Istanbul, on Wednesday 21 October 2026.
    private static readonly DateTime Start = new(2026, 10, 21, 11, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime End = Start.AddHours(1);

    private static ReservationDto Booking(DateTime? start = null, DateTime? end = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), "Board Room", Guid.NewGuid(), "Meeting Room", "3rd Floor", Guid.NewGuid(),
        start ?? Start, end ?? End, ReservationStatus.Confirmed, null, 15, true, null, null);

    private static InboxMessageDto Message(InboxMessageKind kind, string? senderName = "Zeynep Kaya", string? reason = null) => new(
        Guid.NewGuid(), Guid.NewGuid(), kind, Guid.NewGuid(), senderName is null ? null : Guid.NewGuid(),
        "Board Room", Start, End, null, reason, Start, null)
    {
        SenderName = senderName,
    };

    private static PushContent Compose(PushTopic topic, ReservationDto booking, CultureInfo culture) =>
        PushTexts.Compose(new PushNotice(topic, [booking.UserId]) { Reservation = booking }, culture, Istanbul, Grace);

    private static PushContent Compose(InboxMessageDto message, CultureInfo culture) =>
        PushTexts.Compose(new PushNotice(PushTopic.InboxMessage, [message.RecipientId]) { Message = message }, culture, Istanbul, Grace);

    [Fact]
    public void A_reminder_says_which_booking_in_the_readers_language_and_the_offices_time()
    {
        var booking = Booking();

        var turkish = Compose(PushTopic.StartingSoon, booking, Turkish);
        var english = Compose(PushTopic.StartingSoon, booking, English);

        turkish.Title.Should().Be("Rezervasyonunuz birazdan başlıyor");
        turkish.Body.Should().Be("Board Room, 21 Eki Çar 14:00–15:00");
        english.Title.Should().Be("Your booking starts soon");
        english.Body.Should().Be("Board Room, 21 Oct Wed 14:00–15:00");
        english.Url.Should().Be(PushTexts.BookingsUrl);
        english.Tag.Should().Be($"StartingSoon:{booking.Id}");
    }

    [Fact]
    public void A_warning_says_when_the_booking_is_released_and_has_a_tag_of_its_own()
    {
        var booking = Booking();

        var warning = Compose(PushTopic.ReleaseWarning, booking, English);

        warning.Body.Should().Be("Board Room, 21 Oct Wed 14:00–15:00. Unless someone checks in, it's released at 14:15.");
        warning.Tag.Should().NotBe(Compose(PushTopic.StartingSoon, booking, English).Tag);
        Compose(PushTopic.ReleaseWarning, booking, Turkish).Body
            .Should().Be("Board Room, 21 Eki Çar 14:00–15:00. Check-in yapılmazsa 14:15 itibarıyla boşaltılacak.");
    }

    [Fact]
    public void The_release_and_the_end_say_what_happens_now()
    {
        var booking = Booking();

        Compose(PushTopic.Released, booking, English).Body.Should().Be("Board Room, 21 Oct Wed 14:00–15:00: nobody checked in.");
        Compose(PushTopic.EndingSoon, booking, Turkish).Body.Should().Be("Board Room, bitiş 15:00. Uzatmak için dokunun.");
    }

    [Fact]
    public void A_booking_into_the_next_day_names_both_days()
    {
        var overnight = Booking(new DateTime(2026, 10, 21, 19, 0, 0, DateTimeKind.Utc), new DateTime(2026, 10, 22, 6, 0, 0, DateTimeKind.Utc));

        Compose(PushTopic.StartingSoon, overnight, English).Body.Should().Be("Board Room, 21 Oct Wed 22:00 – 22 Oct Thu 09:00");
    }

    [Fact]
    public void An_inbox_message_reads_as_it_does_in_the_inbox_and_opens_it()
    {
        var invitation = Message(InboxMessageKind.MeetingInvitation);

        var english = Compose(invitation, English);
        var turkish = Compose(invitation, Turkish);

        english.Title.Should().Be("Zeynep Kaya invited you to a meeting");
        english.Body.Should().Be("Board Room, 21 Oct Wed 14:00–15:00");
        english.Url.Should().Be(PushTexts.InboxUrl);
        english.Tag.Should().Be($"inbox-{invitation.Id}");
        turkish.Title.Should().Be("Zeynep Kaya sizi bir toplantıya davet etti");
    }

    [Fact]
    public void A_meeting_released_has_nobody_behind_it()
    {
        Compose(Message(InboxMessageKind.MeetingCancelled, senderName: null), Turkish).Title
            .Should().Be("Davetli olduğunuz bir toplantı, kimse check-in yapmadığı için iptal edildi");
        Compose(Message(InboxMessageKind.MeetingCancelled), English).Title
            .Should().Be("Zeynep Kaya cancelled a meeting you were invited to");
    }

    [Fact]
    public void A_decline_gives_its_reason_cut_to_a_glance()
    {
        var reason = new string('x', 200);

        var declined = Compose(Message(InboxMessageKind.BookingDeclined, reason: reason), English);

        declined.Title.Should().Be("Zeynep Kaya declined your booking");
        declined.Body.Should().StartWith("Board Room, 21 Oct Wed 14:00–15:00. xxx").And.EndWith("…");
        declined.Body.Length.Should().Be("Board Room, 21 Oct Wed 14:00–15:00. ".Length + PushTexts.MaxDetailLength);
    }

    [Fact]
    public void A_request_for_approval_opens_the_approvals()
    {
        Compose(Message(InboxMessageKind.ApprovalRequested), English).Url.Should().Be(PushTexts.ApprovalsUrl);
    }

    [Fact]
    public void A_meeting_that_repeats_is_told_from_its_first_date()
    {
        var invitation = Message(InboxMessageKind.SeriesInvitation) with
        {
            Series = new ReservationSeriesDto(Guid.NewGuid(), RecurrenceFrequency.Weekly, 1, WeekDays.Wednesday, new DateOnly(2026, 12, 30), "Europe/Istanbul"),
        };

        Compose(invitation, English).Body.Should().Be("Board Room, from 21 Oct Wed 14:00–15:00");
        Compose(invitation, Turkish).Body.Should().Be("Board Room, 21 Eki Çar 14:00–15:00 itibarıyla");
    }

    [Fact]
    public void A_test_notification_opens_the_profile()
    {
        var test = PushTexts.Compose(new PushNotice(PushTopic.Test, [Guid.NewGuid()]), Turkish, Istanbul, Grace);

        test.Title.Should().Be("Bildirimler açık");
        test.Url.Should().Be(PushTexts.ProfileUrl);
    }
}
