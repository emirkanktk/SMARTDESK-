using Bogus;
using FluentAssertions;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Infrastructure.Persistence.Seed.DemoData;

namespace SmartDesk.Tests.DemoData;

// The demo office's bookings (the plan's day 10): written past the API, so they have to keep its
// rules themselves, and look like an office's.
public class DemoBookingsTests
{
    // Wednesday 14 October 2026, 11:00 in Istanbul.
    private static readonly DateTime Now = new(2026, 10, 14, 8, 0, 0, DateTimeKind.Utc);
    private static readonly OfficeWeek Office = OfficeWeek.Of(OfficeHoursSeedData.Default);
    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(15);

    private static readonly DemoPerson Admin = new(Guid.NewGuid(), null, false, IsAdmin: true);
    private static readonly IReadOnlyList<DemoPerson> People = BuildPeople();
    private static readonly IReadOnlyList<DemoResource> Resources = BuildResources();
    private static readonly DemoBookings Office42 = Generate();

    private static List<DemoPerson> BuildPeople()
    {
        var people = new List<DemoPerson> { Admin };
        foreach (var size in new[] { 6, 6, 5 })
        {
            var manager = Guid.NewGuid();
            people.Add(new(manager, manager, true));
            people.AddRange(Enumerable.Range(0, size).Select(_ => new DemoPerson(Guid.NewGuid(), manager, false)));
        }

        return people;
    }

    // The demo office as the seeder hands it over: Camera C-1 is in maintenance, so it isn't.
    private static List<DemoResource> BuildResources()
    {
        var firstTeam = People[1].Id;
        return DemoOffice.Places
            .Where(p => p.Name != "Camera C-1")
            .Select(p => new DemoResource(
                Guid.NewGuid(),
                p.Name,
                p.TypeId,
                p.Capacity,
                p.CheckIn,
                p.Name is "Meeting Room B" or "Projector P-1" ? firstTeam : null,
                DemoOffice.KeptForTheWalkthrough.Contains(p.Name)))
            .ToList();
    }

    private static DemoBookings Generate(int seed = DemoDataSeeder.Seed)
    {
        var bookings = new DemoBookings(new Faker("tr") { Random = new Randomizer(seed) }, Office, Now, People, Resources);
        bookings.Generate(Admin.Id);
        return bookings;
    }

    private static DemoResource ResourceOf(Reservation r) => Resources.Single(x => x.Id == r.ResourceId);

    private static TimeOnly LocalTime(DateTime utc) =>
        TimeOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Office.Zone));

    [Fact]
    public void NoResourceIsBookedTwiceAtOnce_NorWhileItsBlocked()
    {
        foreach (var resource in Office42.Reservations.Where(r => r.Status == ReservationStatus.Confirmed).GroupBy(r => r.ResourceId))
        {
            var ordered = resource.OrderBy(r => r.StartDateTime).ToList();
            ordered.Zip(ordered.Skip(1)).Should().OnlyContain(pair => pair.First.EndDateTime <= pair.Second.StartDateTime);
        }

        var block = Office42.Blocks.Should().ContainSingle().Subject;
        Office42.Reservations
            .Where(r => r.ResourceId == block.ResourceId && r.Status == ReservationStatus.Confirmed)
            .Should().OnlyContain(r => r.EndDateTime <= block.StartDateTime || r.StartDateTime >= block.EndDateTime);
    }

    [Fact]
    public void TheRoomsTheWalkthroughBooks_AreFreeFromNowOn_WithAPastLikeTheRest()
    {
        var kept = Office42.Reservations.Where(r => ResourceOf(r).KeptFree).ToList();

        kept.Should().NotBeEmpty();
        kept.Should().OnlyContain(r => r.EndDateTime <= Now);
    }

    [Fact]
    public void ATeamsResource_IsBookedByItsTeam_AndNobodyBooksAsTheAdmin()
    {
        var team = People[1].Id;

        Office42.Reservations
            .Where(r => ResourceOf(r).TeamId == team)
            .Should().NotBeEmpty()
            .And.OnlyContain(r => People.Single(p => p.Id == r.UserId).TeamId == team);
        Office42.Reservations.Should().NotContain(r => r.UserId == Admin.Id);
    }

    [Fact]
    public void WhatsOver_WasCheckedInReleasedOrCancelled_AndWhatsToComeIsNeither()
    {
        foreach (var r in Office42.Reservations)
        {
            if (!r.CheckInRequired)
            {
                (r.CheckedInAt, r.ReleasedAt).Should().Be(((DateTime?)null, (DateTime?)null));
            }
            else if (r.StartDateTime > Now)
            {
                r.CheckedInAt.Should().BeNull();
                r.ReleasedAt.Should().BeNull();
            }
            else if (r.ReleasedAt is { } released)
            {
                r.Status.Should().Be(ReservationStatus.Cancelled);
                released.Should().Be(r.StartDateTime + Grace);
                r.EndDateTime.Should().BeOnOrBefore(Now);
            }
            else if (r.Status == ReservationStatus.Confirmed)
            {
                // Under way too: checked in, or the release job would take it at once.
                r.CheckedInAt.Should().NotBeNull().And.BeOnOrBefore(Now);
                r.CheckedInAt!.Value.Should().BeBefore(r.StartDateTime + Grace);
            }
        }
    }

    [Fact]
    public void TheNumbersLookLikeAnOffices()
    {
        var all = Office42.Reservations;
        var over = all.Where(r => r.EndDateTime <= Now).ToList();
        // The visits a check-in was asked for: released, or checked in (ADR-0044).
        var askedToCheckIn = over.Where(r => r.ReleasedAt != null || r.CheckedInAt != null).ToList();
        var noShows = over.Count(r => r.ReleasedAt != null);

        all.Count.Should().BeGreaterThan(500);
        ((double)noShows / askedToCheckIn.Count).Should().BeInRange(0.1, 0.3);
        ((double)over.Count(r => r.Status == ReservationStatus.Cancelled && r.ReleasedAt == null) / over.Count).Should().BeInRange(0.03, 0.12);

        // Busiest mid-week, quietest on Friday.
        int On(DayOfWeek day) => over.Count(r => Office.DateOf(r.StartDateTime).DayOfWeek == day);
        On(DayOfWeek.Wednesday).Should().BeGreaterThan(On(DayOfWeek.Friday));
        On(DayOfWeek.Tuesday).Should().BeGreaterThan(On(DayOfWeek.Friday));
    }

    [Fact]
    public void MeetingsAndDesks_KeepToTheOfficeDay()
    {
        var inOffice = Office42.Reservations.Where(r =>
            ResourceOf(r).TypeId == ResourceTypeSeedData.MeetingRoomId || ResourceOf(r).TypeId == ResourceTypeSeedData.DeskId);

        inOffice.Should().OnlyContain(r =>
            Office.IsWorkday(Office.DateOf(r.StartDateTime).DayOfWeek)
            && LocalTime(r.StartDateTime) >= Office.Opens
            && LocalTime(r.EndDateTime) <= Office.Closes
            && Office.DateOf(r.StartDateTime) == Office.DateOf(r.EndDateTime));
    }

    [Fact]
    public void Meetings_StartThroughTheDay_MostlyMidMorningAndMidAfternoon_AFewADayInEachRoom()
    {
        var over = Office42.Reservations.Where(r => r.EndDateTime <= Now).ToList();
        var meetings = over.Where(r => ResourceOf(r).TypeId == ResourceTypeSeedData.MeetingRoomId && r.SeriesId == null).ToList();
        var workdays = Enumerable.Range(1, DemoBookings.DaysBack)
            .Select(d => Office.DateOf(Now).AddDays(-d))
            .Count(day => Office.IsWorkday(day.DayOfWeek));

        var starts = meetings.GroupBy(r => LocalTime(r.StartDateTime)).OrderByDescending(g => g.Count()).ToList();
        starts.Should().HaveCountGreaterThan(10);
        starts[0].Key.Should().BeOneOf(new TimeOnly(10, 0), new TimeOnly(14, 0));
        meetings.Select(r => (r.EndDateTime - r.StartDateTime).TotalMinutes).Distinct().Should().HaveCountGreaterThan(5);
        ((double)meetings.Count(r => ResourceOf(r).Name == "Meeting Room A") / workdays).Should().BeGreaterThan(2);

        // And every desk is someone's: none stands empty for six weeks.
        Resources.Where(r => r.TypeId == ResourceTypeSeedData.DeskId)
            .Should().OnlyContain(desk => over.Count(r => r.ResourceId == desk.Id) > 5);
    }

    [Fact]
    public void TheSameSeed_GivesTheSameOffice_AndAnotherAnotherOne()
    {
        static IEnumerable<(Guid, Guid, DateTime, DateTime, ReservationStatus)> Shape(DemoBookings b) =>
            b.Reservations.Select(r => (r.ResourceId, r.UserId, r.StartDateTime, r.EndDateTime, r.Status));

        Shape(Generate()).Should().Equal(Shape(Office42));
        Shape(Generate(seed: 7)).Should().NotEqual(Shape(Office42));
    }

    [Fact]
    public void TheWeeklyMeetings_AreSeries_AtTheSameLocalTimeEachWeek_WithOneInvitationEach()
    {
        Office42.Series.Should().HaveCount(2);
        foreach (var series in Office42.Series)
        {
            var dates = Office42.Reservations.Where(r => r.SeriesId == series.Id).OrderBy(r => r.StartDateTime).ToList();
            dates.Should().HaveCountGreaterThan(8);
            dates.Select(r => LocalTime(r.StartDateTime)).Distinct().Should().ContainSingle();
            dates.Zip(dates.Skip(1)).Should().OnlyContain(p => Office.DateOf(p.Second.StartDateTime).DayNumber - Office.DateOf(p.First.StartDateTime).DayNumber == 7);
            series.Until.Should().Be(Office.DateOf(Now).AddDays(28));

            var invited = dates[0].Attendees.Select(a => a.UserId).ToList();
            Office42.Messages
                .Where(m => m.SeriesId == series.Id)
                .Should().OnlyContain(m => m.Kind == InboxMessageKind.SeriesInvitation)
                .And.Subject.Select(m => m.RecipientId).Should().BeEquivalentTo(invited);
        }
    }

    [Fact]
    public void InvitationsToWhatsToCome_AreInTheInboxesOfThoseInvited()
    {
        var invitations = Office42.Messages.Where(m => m.Kind == InboxMessageKind.MeetingInvitation).ToList();

        invitations.Should().NotBeEmpty();
        foreach (var message in invitations)
        {
            var meeting = Office42.Reservations.Single(r => r.Id == message.ReservationId);
            meeting.StartDateTime.Should().BeAfter(Now);
            meeting.Attendees.Should().Contain(a => a.UserId == message.RecipientId);
            message.CreatedAt.Should().BeBefore(Now);
        }
    }

    [Fact]
    public void ThoseWaiting_WaitForATimeSomeoneElseHas()
    {
        Office42.Waiting.Should().HaveCount(2);
        foreach (var entry in Office42.Waiting)
        {
            Office42.Reservations.Should().Contain(r =>
                r.ResourceId == entry.ResourceId
                && r.Status == ReservationStatus.Confirmed
                && r.StartDateTime == entry.StartDateTime
                && r.UserId != entry.UserId);
            entry.StartDateTime.Should().BeAfter(Now);
        }
    }
}
