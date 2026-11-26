using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.WhosIn.Queries.GetWhosIn;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.WhosIn;

// Who's in the office on a day (ADR-0063): everyone a confirmed booking or a meeting they said
// they'd come to brings in, that day in the caller's zone, on what the caller may use.
public sealed class GetWhosInQueryHandlerTests : IDisposable
{
    // 2026-10-05 01:30 in Istanbul (UTC+3), still 4 October in UTC.
    private static readonly DateTime NowUtc = new(2026, 10, 4, 22, 30, 0, DateTimeKind.Utc);

    // 5 October, from midnight in Istanbul.
    private static readonly DateOnly Day = new(2026, 10, 5);
    private static readonly DateTime Midnight = new(2026, 10, 4, 21, 0, 0, DateTimeKind.Utc);

    private readonly SqliteTestDatabase _database = new();
    private readonly Guid _ayse = Guid.NewGuid();
    private readonly Guid _mehmet = Guid.NewGuid();
    private readonly Guid _zeynep = Guid.NewGuid();
    private readonly Guid _okan = Guid.NewGuid();
    private readonly FakeCurrentUser _admin = new(Guid.NewGuid(), Roles.Admin);
    private readonly Mock<IUserAccounts> _accounts = new();
    private readonly List<UserAccount> _people = [];
    private readonly Dictionary<Guid, string> _names;

    public GetWhosInQueryHandlerTests()
    {
        _names = new()
        {
            [_ayse] = "Ayşe Yılmaz",
            [_mehmet] = "Mehmet Demir",
            [_zeynep] = "Zeynep Kaya",
            [_okan] = "Okan Arslan",
        };
        _accounts
            .Setup(a => a.ListAsync(It.IsAny<UserFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserFilter filter, CancellationToken _) => _people
                .Where(p => (filter.Status is null || p.Status == filter.Status) && (filter.ManagerId is null || p.ManagerId == filter.ManagerId))
                .ToList());
    }

    public void Dispose() => _database.Dispose();

    private async Task<WhosInDto> WhosInAsync(FakeCurrentUser caller, DateOnly? date = null, string? timeZone = "Europe/Istanbul")
    {
        await using var context = _database.CreateContext();
        var handler = new GetWhosInQueryHandler(
            context, caller, new FakeIdentityService(_names), _accounts.Object, new FakeTimeProvider(new DateTimeOffset(NowUtc)));
        return await handler.Handle(new GetWhosInQuery(date, timeZone), CancellationToken.None);
    }

    private Task<Reservation> BookAsync(Guid resourceId, Guid userId, double fromHour, double toHour, ReservationStatus status = ReservationStatus.Confirmed) =>
        _database.AddReservationAsync(resourceId, Midnight.AddHours(fromHour), Midnight.AddHours(toHour), status, userId);

    private UserAccount Person(Guid id, string role, Guid? managerId = null, AccountStatus status = AccountStatus.Active) =>
        new(id, "First", "Last", $"{id:N}@company.example", role, status, managerId, null, NowUtc, 0);

    [Fact]
    public async Task Everyone_booked_that_day_is_in_once_with_each_place_earliest_first()
    {
        var desk = await _database.AddResourceAsync("Desk 3-05", ResourceTypeSeedData.DeskId);
        var room = await _database.AddResourceAsync("Board Room");
        await BookAsync(room.Id, _ayse, 14, 15);
        var deskDay = await BookAsync(desk.Id, _ayse, 9, 17);
        await BookAsync(room.Id, _mehmet, 9, 10);
        // From the evening before into this day: in.
        await BookAsync(desk.Id, _okan, -1, 2);
        // The evening before, cancelled, waiting for approval, and the day after: not in.
        await BookAsync(room.Id, _zeynep, -3, -2);
        await BookAsync(room.Id, _zeynep, 11, 12, ReservationStatus.Cancelled);
        await BookAsync(room.Id, _zeynep, 12, 13, ReservationStatus.Pending);
        await BookAsync(room.Id, _zeynep, 24, 25);
        await using (var context = _database.CreateContext())
        {
            await context.Reservations.Where(r => r.Id == deskDay.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(r => r.CheckedInAt, Midnight.AddHours(9.1)));
        }

        var whosIn = await WhosInAsync(_admin, Day);

        whosIn.Date.Should().Be(Day);
        whosIn.TimeZone.Should().Be("Europe/Istanbul");
        whosIn.People.Select(p => p.Name).Should().Equal("Ayşe Yılmaz", "Mehmet Demir", "Okan Arslan");
        var ayse = whosIn.People[0];
        ayse.Visits.Select(v => (v.ResourceName, v.StartDateTime, v.EndDateTime, v.CheckedIn)).Should().Equal(
            ("Desk 3-05", Midnight.AddHours(9), Midnight.AddHours(17), true),
            ("Board Room", Midnight.AddHours(14), Midnight.AddHours(15), false));
        ayse.Visits[0].Should().Match<OfficeVisitDto>(v =>
            v.ResourceTypeName == "Desk" && v.Location == "2nd Floor" && v.ResourceId == desk.Id && !v.Invited && v.OrganizerName == null);
        whosIn.People.Should().OnlyContain(p => !p.IsMe && !p.InMyTeam);
        whosIn.HasTeam.Should().BeFalse("an Admin is in no team");
    }

    [Fact]
    public async Task Someone_who_said_they_d_come_to_a_meeting_is_in_with_whose_it_is()
    {
        var room = await _database.AddResourceAsync("Board Room");
        var meeting = await BookAsync(room.Id, _zeynep, 14, 15);
        await using (var context = _database.CreateContext())
        {
            context.ReservationAttendees.AddRange(
                new ReservationAttendee { ReservationId = meeting.Id, UserId = _ayse, Response = AttendeeResponse.Accepted },
                new ReservationAttendee { ReservationId = meeting.Id, UserId = _mehmet, Response = AttendeeResponse.Declined },
                new ReservationAttendee { ReservationId = meeting.Id, UserId = _okan, Response = AttendeeResponse.Pending });
            await context.SaveChangesAsync();
        }

        var whosIn = await WhosInAsync(_admin, Day);

        whosIn.People.Select(p => p.Name).Should().Equal("Ayşe Yılmaz", "Zeynep Kaya");
        whosIn.People[0].Visits.Should().ContainSingle().Which.Should().Match<OfficeVisitDto>(v =>
            v.ReservationId == meeting.Id && v.Invited && v.OrganizerName == "Zeynep Kaya");
        whosIn.People[1].Visits.Should().ContainSingle().Which.Invited.Should().BeFalse();
    }

    [Fact]
    public async Task Only_bookings_on_what_the_caller_may_use_and_of_accounts_still_there()
    {
        var shared = await _database.AddResourceAsync("Desk 1-01", ResourceTypeSeedData.DeskId);
        var okansRoom = await _database.AddResourceAsync("Okan's room");
        await using (var context = _database.CreateContext())
        {
            context.ResourceTeams.Add(new ResourceTeam { ResourceId = okansRoom.Id, TeamId = _okan });
            await context.SaveChangesAsync();
        }

        await BookAsync(shared.Id, _mehmet, 9, 17);
        await BookAsync(okansRoom.Id, _okan, 10, 11);
        await BookAsync(shared.Id, Guid.NewGuid(), 8, 9);

        var whosIn = await WhosInAsync(new FakeCurrentUser(_ayse, Roles.Employee) { TeamId = _zeynep }, Day);

        whosIn.People.Select(p => p.Name).Should().Equal("Mehmet Demir");
    }

    [Fact]
    public async Task The_callers_team_is_marked_the_caller_too()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        var waiting = Guid.NewGuid();
        _names[waiting] = "Deniz Er";
        _people.AddRange(
        [
            Person(_zeynep, Roles.Manager),
            Person(_ayse, Roles.Employee, _zeynep),
            Person(_mehmet, Roles.Employee, _zeynep),
            Person(_okan, Roles.Manager),
            Person(waiting, Roles.Employee, _zeynep, AccountStatus.PendingApproval),
        ]);
        foreach (var (who, hour) in new[] { (_ayse, 9), (_mehmet, 10), (_zeynep, 11), (_okan, 12), (waiting, 13) })
        {
            await BookAsync(room.Id, who, hour, hour + 1);
        }

        var whosIn = await WhosInAsync(new FakeCurrentUser(_ayse, Roles.Employee) { TeamId = _zeynep }, Day);

        whosIn.HasTeam.Should().BeTrue();
        whosIn.People.Select(p => (p.Name, p.IsMe, p.InMyTeam)).Should().Equal(
            ("Ayşe Yılmaz", true, true),
            ("Deniz Er", false, false),
            ("Mehmet Demir", false, true),
            ("Okan Arslan", false, false),
            ("Zeynep Kaya", false, true));
        (await WhosInAsync(new FakeCurrentUser(_zeynep, Roles.Manager) { TeamId = _zeynep }, Day))
            .People.Where(p => p.InMyTeam).Select(p => p.Name).Should().Equal("Ayşe Yılmaz", "Mehmet Demir", "Zeynep Kaya");
    }

    [Fact]
    public async Task Without_a_date_it_is_today_where_the_caller_is()
    {
        (await WhosInAsync(_admin)).Date.Should().Be(Day, "it's already 5 October in Istanbul");

        var utc = await WhosInAsync(_admin, timeZone: null);

        utc.Date.Should().Be(new DateOnly(2026, 10, 4));
        utc.TimeZone.Should().Be("UTC");
    }

    [Fact]
    public async Task Names_come_in_the_order_of_the_requests_language()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        _names[_ayse] = "İpek Er";
        _names[_mehmet] = "Işıl Ay";
        await BookAsync(room.Id, _ayse, 9, 10);
        await BookAsync(room.Id, _mehmet, 10, 11);

        using (UiCulture.Use("tr-TR"))
        {
            (await WhosInAsync(_admin, Day)).People.Select(p => p.Name).Should().Equal("Işıl Ay", "İpek Er");
        }

        (await WhosInAsync(_admin, Day)).People.Select(p => p.Name).Should().Equal("İpek Er", "Işıl Ay");
    }

    [Theory]
    [InlineData("Europe/Istanbul", 2026, true)]
    [InlineData(null, 2100, true)]
    [InlineData("Mars/Olympus_Mons", 2026, false)]
    [InlineData(null, 1, false)]
    [InlineData(null, 9999, false)]
    public void Validator_takes_known_zones_and_days_this_side_of_the_calendars_ends(string? zone, int year, bool valid) =>
        new GetWhosInQueryValidator().Validate(new GetWhosInQuery(new DateOnly(year, 12, 31), zone)).IsValid.Should().Be(valid);
}
