using System.Globalization;
using System.Text;
using FluentAssertions;
using Ical.Net.CalendarComponents;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Calendar.Commands.CreateCalendarSubscription;
using SmartDesk.Application.Features.Calendar.Commands.DeleteCalendarSubscription;
using SmartDesk.Application.Features.Calendar.Common;
using SmartDesk.Application.Features.Calendar.Queries.GetCalendarFeed;
using SmartDesk.Application.Features.Calendar.Queries.GetCalendarSubscription;
using SmartDesk.Application.Features.Calendar.Queries.GetReservationCalendar;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;
using SmartDesk.Infrastructure;
using SmartDesk.Infrastructure.Calendar;
using SmartDesk.Tests.TestSupport;
using IcalCalendar = Ical.Net.Calendar;

namespace SmartDesk.Tests.Calendar;

// Bookings in a calendar app (ADR-0048): one added as an .ics file, and the personal link a
// calendar app subscribes to.
public sealed class CalendarTests : IDisposable
{
    // Thursday 15 October 2026, 11:00 in Istanbul.
    private static readonly DateTime Now = new(2026, 10, 15, 8, 0, 0, DateTimeKind.Utc);
    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee);
    private static readonly FakeCurrentUser Mehmet = new(Guid.NewGuid(), Roles.Employee);
    private static readonly FakeCurrentUser Zeynep = new(Guid.NewGuid(), Roles.Employee);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Now));
    private readonly Mock<IUserAccounts> _accounts = new();
    private readonly Dictionary<Guid, (AccountStatus Status, string? Language)> _people = new()
    {
        [Ayse.UserId!.Value] = (AccountStatus.Active, "en"),
        [Mehmet.UserId!.Value] = (AccountStatus.Active, "en"),
        [Zeynep.UserId!.Value] = (AccountStatus.Active, "en"),
    };
    private readonly FakeIdentityService _names = new(new Dictionary<Guid, string>
    {
        [Ayse.UserId!.Value] = "Ayşe Yılmaz",
        [Mehmet.UserId!.Value] = "Mehmet Demir",
        [Zeynep.UserId!.Value] = "Zeynep Kaya",
    });
    private readonly IFrontendLinks _links =
        new FrontendLinks(Options.Create(new AppOptions { FrontendUrl = "http://localhost:4200" }));

    public CalendarTests()
    {
        _accounts
            .Setup(a => a.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => _people.TryGetValue(id, out var person)
                ? new UserAccount(id, "First", "Last", $"{id:N}@example.com", Roles.Employee, person.Status, null, null, Now, 0)
                {
                    Language = person.Language,
                }
                : null);
    }

    public void Dispose() => _database.Dispose();

    private async Task<Reservation> BookAsync(
        FakeCurrentUser owner,
        double daysFromNow = 1,
        ReservationStatus status = ReservationStatus.Confirmed,
        string? note = null,
        bool checkIn = false,
        params (FakeCurrentUser Who, AttendeeResponse Response)[] invited)
    {
        var room = await _database.AddResourceAsync($"Room {Guid.NewGuid():N}"[..12]);
        await using var context = _database.CreateContext();
        var reservation = new Reservation
        {
            ResourceId = room.Id,
            UserId = owner.UserId!.Value,
            StartDateTime = Now.AddDays(daysFromNow),
            EndDateTime = Now.AddDays(daysFromNow).AddHours(1),
            Status = status,
            Note = note,
            CheckInRequired = checkIn,
        };
        context.Reservations.Add(reservation);
        foreach (var (who, response) in invited)
        {
            context.ReservationAttendees.Add(new ReservationAttendee { Reservation = reservation, UserId = who.UserId!.Value, Response = response });
        }

        if (checkIn)
        {
            (await context.Resources.SingleAsync(r => r.Id == room.Id)).CheckInRequired = true;
        }

        await context.SaveChangesAsync();
        reservation.Resource = room;
        return reservation;
    }

    private async Task<CalendarDocument> AddToCalendarAsync(Guid reservationId, FakeCurrentUser who)
    {
        await using var context = _database.CreateContext();
        return await new GetReservationCalendarQueryHandler(context, who, _names, _links, TestCheckInTimes.Default, _clock)
            .Handle(new GetReservationCalendarQuery(reservationId), CancellationToken.None);
    }

    private async Task<CalendarSubscriptionDto> SubscribeAsync(FakeCurrentUser who)
    {
        await using var context = _database.CreateContext();
        return await new CreateCalendarSubscriptionCommandHandler(context, who, _links, _clock)
            .Handle(new CreateCalendarSubscriptionCommand(), CancellationToken.None);
    }

    private async Task<CalendarDocument?> FetchAsync(string? token)
    {
        await using var context = _database.CreateContext();
        return await new GetCalendarFeedQueryHandler(context, _accounts.Object, _names, _links, TestCheckInTimes.Default, _clock)
            .Handle(new GetCalendarFeedQuery(token), CancellationToken.None);
    }

    private static string TokenOf(CalendarSubscriptionDto subscription)
    {
        var url = new Uri(subscription.Url!);
        url.AbsolutePath.Should().Be("/api/calendar/feed.ics");
        return url.Query.Should().StartWith("?token=").And.Subject[7..];
    }

    [Fact]
    public async Task ABookingAdded_IsTheResourceAtItsTime_WithTheNoteTheCheckInAndALinkBack()
    {
        var booking = await BookAsync(Ayse, note: "Sprint planning", checkIn: true);

        var calendar = await AddToCalendarAsync(booking.Id, Ayse);

        calendar.Name.Should().Be("SmartDesk bookings");
        calendar.RefreshInterval.Should().BeNull("a file is added once");
        var entry = calendar.Entries.Should().ContainSingle().Subject;
        entry.Uid.Should().Be($"{booking.Id}@smartdesk");
        (entry.Start, entry.End).Should().Be((booking.StartDateTime, booking.EndDateTime));
        entry.Summary.Should().Be(booking.Resource.Name);
        entry.Location.Should().Be("2nd Floor");
        entry.Url.Should().Be($"http://localhost:4200/reservations/{booking.Id}/edit");
        entry.Status.Should().Be(CalendarEntryStatus.Confirmed);
        entry.Description.Should().Be(
            "Sprint planning\n\n"
            + "Check in at the door no later than 15 minutes after the start; if nobody does, the booking is cancelled.\n\n"
            + $"In SmartDesk: http://localhost:4200/reservations/{booking.Id}/edit");
    }

    [Fact]
    public async Task SomeoneInvited_ReadsWhoInvitedThem_AndTheirAnswer_TentativeUntilTheyAnswer()
    {
        var meeting = await BookAsync(Ayse, invited: [(Mehmet, AttendeeResponse.Pending), (Zeynep, AttendeeResponse.Accepted)]);

        var mehmet = (await AddToCalendarAsync(meeting.Id, Mehmet)).Entries.Single();
        var zeynep = (await AddToCalendarAsync(meeting.Id, Zeynep)).Entries.Single();
        var ayse = (await AddToCalendarAsync(meeting.Id, Ayse)).Entries.Single();

        mehmet.Status.Should().Be(CalendarEntryStatus.Tentative);
        mehmet.Description.Should().StartWith(
            "Ayşe Yılmaz invited you. You haven't answered yet.\n\nInvited: Mehmet Demir, Zeynep Kaya\n\n");
        zeynep.Status.Should().Be(CalendarEntryStatus.Confirmed);
        zeynep.Description.Should().StartWith("Ayşe Yılmaz invited you. You said you're coming.");
        ayse.Status.Should().Be(CalendarEntryStatus.Confirmed);
        ayse.Description.Should().StartWith("Invited: Mehmet Demir, Zeynep Kaya\n\n", "the organizer invited no one but these");
        new[] { mehmet.Uid, zeynep.Uid }.Should().AllBe(ayse.Uid, "one meeting, one event, in whoever's calendar");
    }

    [Fact]
    public async Task AnotherPersonsBooking_IsNotFound_AndACancelledOne_ComesAsCancelled()
    {
        var booking = await BookAsync(Ayse);
        var cancelled = await BookAsync(Ayse, status: ReservationStatus.Cancelled);
        var declined = await BookAsync(Ayse, invited: [(Mehmet, AttendeeResponse.Declined)]);

        var act = () => AddToCalendarAsync(booking.Id, Zeynep);

        await act.Should().ThrowAsync<NotFoundException>();
        (await AddToCalendarAsync(cancelled.Id, Ayse)).Entries.Single().Status.Should().Be(CalendarEntryStatus.Cancelled);
        var notComing = (await AddToCalendarAsync(declined.Id, Mehmet)).Entries.Single();
        notComing.Status.Should().Be(CalendarEntryStatus.Cancelled);
        notComing.Description.Should().StartWith("Ayşe Yılmaz invited you. You said you can't come.");
    }

    [Fact]
    public async Task InTurkish_WhenTheAppIs()
    {
        var meeting = await BookAsync(Ayse, checkIn: true, invited: [(Mehmet, AttendeeResponse.Pending)]);
        using var _ = UiCulture.Use("tr-TR");

        var calendar = await AddToCalendarAsync(meeting.Id, Mehmet);

        calendar.Name.Should().Be("SmartDesk rezervasyonları");
        calendar.Entries.Single().Description.Should().Be(
            "Ayşe Yılmaz sizi davet etti. Henüz yanıt vermediniz.\n\n"
            + "Davetliler: Mehmet Demir\n\n"
            + "En geç başlangıçtan 15 dakika sonra kapıda check-in yapın; kimse yapmazsa rezervasyon iptal edilir.\n\n"
            + $"SmartDesk'te: http://localhost:4200/reservations/{meeting.Id}/edit");
    }

    [Fact]
    public async Task TheFeed_HasTheirBookingsAndTheMeetingsTheyHaventTurnedDown_FromNinetyDaysAgoOn()
    {
        var next = await BookAsync(Ayse, daysFromNow: 1);
        var lastMonth = await BookAsync(Ayse, daysFromNow: -30);
        await BookAsync(Ayse, daysFromNow: -CalendarFeedRules.DaysBack - 1);
        await BookAsync(Ayse, daysFromNow: 2, status: ReservationStatus.Cancelled);
        var invited = await BookAsync(Mehmet, daysFromNow: 3, invited: [(Ayse, AttendeeResponse.Pending)]);
        await BookAsync(Mehmet, daysFromNow: 4, invited: [(Ayse, AttendeeResponse.Declined)]);
        await BookAsync(Mehmet, daysFromNow: 5);
        var link = await SubscribeAsync(Ayse);

        var feed = await FetchAsync(TokenOf(link));

        feed.Should().NotBeNull();
        feed!.RefreshInterval.Should().Be(TimeSpan.FromHours(1));
        feed.Entries.Select(e => e.Uid).Should().Equal(
            CalendarEntries.Uid(lastMonth.Id), CalendarEntries.Uid(next.Id), CalendarEntries.Uid(invited.Id));
        feed.Entries.Last().Status.Should().Be(CalendarEntryStatus.Tentative);

        await using var context = _database.CreateContext();
        (await context.CalendarFeeds.SingleAsync()).LastFetchedAt.Should().Be(Now, "its owner sees that it's in use");
    }

    [Fact]
    public async Task TheFeed_IsInItsOwnersLanguage_WhateverTheRequestsIs()
    {
        _people[Ayse.UserId!.Value] = (AccountStatus.Active, "tr");
        await BookAsync(Ayse);
        var link = await SubscribeAsync(Ayse);

        var feed = await FetchAsync(TokenOf(link));

        feed!.Name.Should().Be("SmartDesk rezervasyonları");
        feed.Entries.Single().Description.Should().StartWith("SmartDesk'te: ");
        CultureInfo.CurrentUICulture.Name.Should().Be("en-US", "the request's own language is left as it was");
    }

    [Fact]
    public async Task ANewLink_ReplacesTheOld_WhichStopsWorking_AndIsNeverShownAgain()
    {
        await BookAsync(Ayse);
        var first = await SubscribeAsync(Ayse);
        await FetchAsync(TokenOf(first));
        _clock.Advance(TimeSpan.FromDays(1));

        var second = await SubscribeAsync(Ayse);

        TokenOf(second).Should().NotBe(TokenOf(first)).And.MatchRegex("^[0-9a-f]{64}$");
        second.CreatedAt.Should().Be(Now.AddDays(1));
        (await FetchAsync(TokenOf(first))).Should().BeNull();
        (await FetchAsync(TokenOf(second))).Should().NotBeNull();

        await using var context = _database.CreateContext();
        var shown = await new GetCalendarSubscriptionQueryHandler(context, Ayse)
            .Handle(new GetCalendarSubscriptionQuery(), CancellationToken.None);
        shown.Should().Be(new CalendarSubscriptionDto(null, Now.AddDays(1), Now.AddDays(1)));
        (await context.CalendarFeeds.SingleAsync()).TokenHash.Should().Be(CalendarFeedRules.HashToken(TokenOf(second)));
    }

    [Fact]
    public async Task NothingComes_ForARemovedLink_ADisabledAccount_OrATokenThatIsNotALinks()
    {
        await BookAsync(Ayse);
        var removed = await SubscribeAsync(Ayse);
        await using (var context = _database.CreateContext())
        {
            await new DeleteCalendarSubscriptionCommandHandler(context, Ayse)
                .Handle(new DeleteCalendarSubscriptionCommand(), CancellationToken.None);
            (await new GetCalendarSubscriptionQueryHandler(context, Ayse)
                .Handle(new GetCalendarSubscriptionQuery(), CancellationToken.None)).Should().BeNull();
        }

        var disabled = await SubscribeAsync(Mehmet);
        _people[Mehmet.UserId!.Value] = (AccountStatus.Disabled, "en");

        (await FetchAsync(TokenOf(removed))).Should().BeNull();
        (await FetchAsync(TokenOf(disabled))).Should().BeNull();
        (await FetchAsync(null)).Should().BeNull();
        (await FetchAsync("not-a-token")).Should().BeNull();
        (await FetchAsync(TokenOf(disabled).ToUpperInvariant())).Should().BeNull();
        (await FetchAsync(CalendarFeedRules.NewToken())).Should().BeNull();
    }

    [Fact]
    public void TheFile_HasUtcTimes_TextAsWritten_AndAFeedSaysHowOftenToFetchIt()
    {
        var start = new DateTime(2026, 10, 16, 7, 0, 0, DateTimeKind.Utc);
        var description = "Agenda: budget, hiring; and the rest\n\nIn SmartDesk: http://localhost:4200/reservations/1/edit "
            + new string('x', 120);
        var document = new CalendarDocument("SmartDesk bookings", Now,
        [
            new CalendarEntry("one@smartdesk", start, start.AddHours(1), "Toplantı odası, 3. kat", "", description,
                "http://localhost:4200/reservations/1/edit", CalendarEntryStatus.Tentative),
        ])
        {
            RefreshInterval = TimeSpan.FromHours(1),
        };

        var text = Encoding.UTF8.GetString(new IcalCalendarWriter().Write(document));

        text.Should().Contain("METHOD:PUBLISH\r\n")
            .And.Contain("X-WR-CALNAME:SmartDesk bookings\r\n")
            .And.Contain("REFRESH-INTERVAL;VALUE=DURATION:PT1H\r\n")
            .And.Contain("X-PUBLISHED-TTL:PT1H\r\n")
            .And.Contain("DTSTART:20261016T070000Z\r\n")
            .And.Contain("DTEND:20261016T080000Z\r\n")
            .And.Contain("STATUS:TENTATIVE\r\n")
            .And.NotContain("LOCATION", "no place is better than an empty one");
        text.Split("\r\n").Should().OnlyContain(line => Encoding.UTF8.GetByteCount(line) <= 75, "long lines are folded");

        var read = IcalCalendar.Load(text)!.Events.Single();
        read.Uid.Should().Be("one@smartdesk");
        read.Summary.Should().Be("Toplantı odası, 3. kat");
        read.Description.Should().Be(description);
        read.DtStart!.AsUtc.Should().Be(start);
        read.Url.Should().Be(new Uri("http://localhost:4200/reservations/1/edit"));
    }

    [Fact]
    public void AToken_IsSixtyFourHexDigits_KeptAsItsHash()
    {
        var token = CalendarFeedRules.NewToken();

        CalendarFeedRules.LooksLikeToken(token).Should().BeTrue();
        CalendarFeedRules.HashToken(token).Should().MatchRegex("^[0-9a-f]{64}$").And.NotBe(token)
            .And.Be(CalendarFeedRules.HashToken(token));
        CalendarFeedRules.NewToken().Should().NotBe(token);
        CalendarFeedRules.LooksLikeToken(token[..63]).Should().BeFalse();
        CalendarFeedRules.LooksLikeToken(token[..63] + "g").Should().BeFalse();
    }
}
