using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.LobbyScreens.Commands.CreateLobbyScreen;
using SmartDesk.Application.Features.LobbyScreens.Commands.DeleteLobbyScreen;
using SmartDesk.Application.Features.LobbyScreens.Common;
using SmartDesk.Application.Features.LobbyScreens.Queries.GetLobby;
using SmartDesk.Application.Features.LobbyScreens.Queries.GetLobbyScreens;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;
using SmartDesk.Infrastructure;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.LobbyScreens;

// Lobby screens (ADR-0064): set up by an Admin with a link shown once, and showing, with nobody
// signed in, the office's rooms, its free desks and how many people are in.
public sealed class LobbyScreenTests : IDisposable
{
    // Thursday 26 November 2026, 10:00 in Istanbul (UTC+3).
    private static readonly DateTime NowUtc = new(2026, 11, 26, 7, 0, 0, DateTimeKind.Utc);
    private const string Istanbul = "Europe/Istanbul";

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(NowUtc));
    private readonly FrontendLinks _links = new(Options.Create(new AppOptions { FrontendUrl = "http://localhost:4200" }));

    public void Dispose() => _database.Dispose();

    private async Task<NewLobbyScreenDto> CreateAsync(string name = "Entrance", string? location = null)
    {
        await using var context = _database.CreateContext();
        return await new CreateLobbyScreenCommandHandler(context, _links, _clock)
            .Handle(new CreateLobbyScreenCommand(name, location), CancellationToken.None);
    }

    private async Task<LobbyDto> LobbyAsync(string? token, string? timeZone = Istanbul)
    {
        await using var context = _database.CreateContext();
        return await new GetLobbyQueryHandler(context, _links, _clock)
            .Handle(new GetLobbyQuery(token, timeZone), CancellationToken.None);
    }

    private static string TokenOf(NewLobbyScreenDto created) => created.Link[(created.Link.IndexOf('#') + 1)..];

    private async Task<Resource> AddPlaceAsync(
        string name, Guid typeId, string location, ResourceStatus status = ResourceStatus.Active, int capacity = 8)
    {
        await using var context = _database.CreateContext();
        var resource = new Resource
        {
            Name = name,
            ResourceTypeId = typeId,
            Capacity = capacity,
            Location = location,
            Status = status,
        };
        context.Resources.Add(resource);
        await context.SaveChangesAsync();
        return resource;
    }

    private Task<Reservation> BookAsync(
        Guid resourceId, double fromHour, double toHour, Guid? userId = null, ReservationStatus status = ReservationStatus.Confirmed) =>
        _database.AddReservationAsync(resourceId, NowUtc.AddHours(fromHour), NowUtc.AddHours(toHour), status, userId);

    [Fact]
    public async Task A_screen_is_set_up_with_a_link_shown_once_and_only_its_hash_kept()
    {
        await AddPlaceAsync("Board Room", ResourceTypeIds.MeetingRoom, "5th Floor");

        var created = await CreateAsync("  Entrance  ", "5th Floor");

        created.Link.Should().MatchRegex("^http://localhost:4200/lobby#[0-9a-f]{64}$");
        created.Screen.Should().Match<LobbyScreenDto>(s =>
            s.Name == "Entrance" && s.Location == "5th Floor" && s.CreatedAt == NowUtc && s.LastSeenAt == null);
        await using var context = _database.CreateContext();
        var stored = await context.LobbyScreens.SingleAsync();
        stored.TokenHash.Should().Be(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(TokenOf(created)))));
    }

    [Fact]
    public async Task A_floor_with_no_resources_is_refused_and_so_is_one_screen_too_many()
    {
        var unknown = () => CreateAsync("Kitchen", "9th Floor");
        (await unknown.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.PropertyName == "Location" && e.ErrorMessage == "No resource is on that floor.");

        for (var i = 0; i < LobbyScreenRules.MaxScreens; i++)
        {
            await CreateAsync($"Screen {i}");
        }

        var oneMore = () => CreateAsync("One more");
        (await oneMore.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be(ConflictCodes.LobbyScreensFull);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("Entrance\nhall", false)]
    [InlineData("Entrance", true)]
    public void A_name_is_needed_on_one_line(string name, bool valid) =>
        new CreateLobbyScreenCommandValidator().Validate(new CreateLobbyScreenCommand(name, null)).IsValid.Should().Be(valid);

    [Fact]
    public async Task The_screens_are_listed_by_name_and_one_taken_down_stops_showing()
    {
        var kitchen = await CreateAsync("Kitchen");
        await CreateAsync("Entrance");

        await using (var context = _database.CreateContext())
        {
            (await new GetLobbyScreensQueryHandler(context).Handle(new GetLobbyScreensQuery(), CancellationToken.None))
                .Select(s => s.Name).Should().Equal("Entrance", "Kitchen");
            await new DeleteLobbyScreenCommandHandler(context).Handle(new DeleteLobbyScreenCommand(kitchen.Screen.Id), CancellationToken.None);
        }

        var shown = () => LobbyAsync(TokenOf(kitchen));
        await shown.Should().ThrowAsync<NotFoundException>();
        await using var again = _database.CreateContext();
        var twice = () => new DeleteLobbyScreenCommandHandler(again).Handle(new DeleteLobbyScreenCommand(kitchen.Screen.Id), CancellationToken.None);
        await twice.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-token")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task A_token_that_isnt_a_screens_shows_nothing(string? token)
    {
        await CreateAsync();

        var shown = () => LobbyAsync(token);

        await shown.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task It_shows_the_rooms_with_the_rest_of_today_and_nobodys_name()
    {
        var board = await AddPlaceAsync("Board Room", ResourceTypeIds.MeetingRoom, "5th Floor", capacity: 16);
        var roomA = await AddPlaceAsync("Meeting Room A", ResourceTypeIds.MeetingRoom, "2nd Floor");
        await AddPlaceAsync("Meeting Room C", ResourceTypeIds.MeetingRoom, "2nd Floor", ResourceStatus.Maintenance);
        await AddPlaceAsync("Old Room", ResourceTypeIds.MeetingRoom, "2nd Floor", ResourceStatus.Inactive);
        await AddPlaceAsync("Desk 3-01", ResourceTypeIds.Desk, "3rd Floor");
        await AddPlaceAsync("Company Car", ResourceTypeIds.CompanyCar, "Parking");
        await BookAsync(board.Id, -1, 1);
        await BookAsync(board.Id, 4, 5, status: ReservationStatus.Pending);
        await BookAsync(board.Id, 5, 6, status: ReservationStatus.Cancelled);
        // Ended already, and tomorrow (Istanbul): not shown.
        await BookAsync(board.Id, -3, -2);
        await BookAsync(board.Id, 24, 25);
        await _database.AddBlockAsync(roomA.Id, NowUtc.AddHours(2), NowUtc.AddHours(3), "Cleaning");
        var created = await CreateAsync();

        var lobby = await LobbyAsync(TokenOf(created));

        lobby.ScreenName.Should().Be("Entrance");
        lobby.Location.Should().BeNull();
        lobby.Now.Should().Be(NowUtc);
        lobby.AppUrl.Should().Be("http://localhost:4200/");
        lobby.Rooms.Select(r => (r.Name, r.Status)).Should().Equal(
            ("Meeting Room A", ResourceStatus.Active),
            ("Meeting Room C", ResourceStatus.Maintenance),
            ("Board Room", ResourceStatus.Active));
        var boardRoom = lobby.Rooms.Single(r => r.Name == "Board Room");
        boardRoom.Should().Match<LobbyRoomDto>(r => r.Location == "5th Floor" && r.Capacity == 16);
        boardRoom.Bookings.Select(b => b.StartDateTime).Should().Equal(NowUtc.AddHours(-1), NowUtc.AddHours(4));
        lobby.Rooms.Single(r => r.Name == "Meeting Room A").Blocks.Should().ContainSingle().Which.Reason.Should().Be("Cleaning");
    }

    [Fact]
    public async Task It_counts_each_floors_free_desks_and_the_people_in_today()
    {
        var d1 = await AddPlaceAsync("Desk 3-01", ResourceTypeIds.Desk, "3rd Floor", capacity: 1);
        var d2 = await AddPlaceAsync("Desk 3-02", ResourceTypeIds.Desk, "3rd Floor", capacity: 1);
        await AddPlaceAsync("Desk 3-03", ResourceTypeIds.Desk, "3rd Floor", capacity: 1);
        await AddPlaceAsync("Desk 3-04", ResourceTypeIds.Desk, "3rd Floor", ResourceStatus.Maintenance, capacity: 1);
        var d5 = await AddPlaceAsync("Desk 5-01", ResourceTypeIds.Desk, "5th Floor", capacity: 1);
        var room = await AddPlaceAsync("Board Room", ResourceTypeIds.MeetingRoom, "5th Floor");
        var ayse = Guid.NewGuid();
        var mehmet = Guid.NewGuid();
        var zeynep = Guid.NewGuid();
        // Taken now: a booking under way, and a blocked window. One later today leaves a desk free now.
        await BookAsync(d1.Id, -1, 7, ayse);
        await _database.AddBlockAsync(d2.Id, NowUtc.AddHours(-1), NowUtc.AddHours(1));
        await BookAsync(d5.Id, 3, 5, mehmet);
        var meeting = await BookAsync(room.Id, 4, 5, zeynep);
        await using (var context = _database.CreateContext())
        {
            context.ReservationAttendees.AddRange(
                new ReservationAttendee { ReservationId = meeting.Id, UserId = ayse, Response = AttendeeResponse.Accepted },
                new ReservationAttendee { ReservationId = meeting.Id, UserId = Guid.NewGuid(), Response = AttendeeResponse.Accepted },
                new ReservationAttendee { ReservationId = meeting.Id, UserId = Guid.NewGuid(), Response = AttendeeResponse.Declined });
            await context.SaveChangesAsync();
        }

        // Not in: waiting for approval, cancelled, and tomorrow.
        await BookAsync(d5.Id, 6, 7, Guid.NewGuid(), ReservationStatus.Pending);
        await BookAsync(d5.Id, 7, 8, Guid.NewGuid(), ReservationStatus.Cancelled);
        await BookAsync(d5.Id, 30, 31, Guid.NewGuid());
        var created = await CreateAsync();

        var lobby = await LobbyAsync(TokenOf(created));

        lobby.Desks.Should().Equal(new LobbyDesksDto("3rd Floor", 1, 3), new LobbyDesksDto("5th Floor", 1, 1));
        lobby.PeopleToday.Should().Be(4, "Ayşe, Mehmet, Zeynep and the guest who said they'd come");
    }

    [Fact]
    public async Task A_screen_on_a_floor_shows_that_floor_but_counts_the_whole_office()
    {
        await AddPlaceAsync("Board Room", ResourceTypeIds.MeetingRoom, "5th Floor");
        var roomA = await AddPlaceAsync("Meeting Room A", ResourceTypeIds.MeetingRoom, "2nd Floor");
        await AddPlaceAsync("Desk 5-01", ResourceTypeIds.Desk, "5th Floor", capacity: 1);
        await AddPlaceAsync("Desk 2-01", ResourceTypeIds.Desk, "2nd Floor", capacity: 1);
        await BookAsync(roomA.Id, 1, 2, Guid.NewGuid());
        var created = await CreateAsync("5th floor kitchen", "5th Floor");

        var lobby = await LobbyAsync(TokenOf(created));

        lobby.Location.Should().Be("5th Floor");
        lobby.Rooms.Select(r => r.Name).Should().Equal("Board Room");
        lobby.Desks.Select(d => d.Location).Should().Equal("5th Floor");
        lobby.PeopleToday.Should().Be(1);
    }

    [Fact]
    public async Task It_notes_when_the_screen_last_asked_once_a_minute_at_most()
    {
        var created = await CreateAsync();
        async Task<DateTime?> LastSeenAsync()
        {
            await using var context = _database.CreateContext();
            return (await context.LobbyScreens.SingleAsync()).LastSeenAt;
        }

        await LobbyAsync(TokenOf(created));
        (await LastSeenAsync()).Should().Be(NowUtc);

        _clock.Advance(TimeSpan.FromSeconds(30));
        await LobbyAsync(TokenOf(created));
        (await LastSeenAsync()).Should().Be(NowUtc, "it asked again within the minute");

        _clock.Advance(TimeSpan.FromSeconds(30));
        await LobbyAsync(TokenOf(created));
        (await LastSeenAsync()).Should().Be(NowUtc.AddMinutes(1));
    }
}
