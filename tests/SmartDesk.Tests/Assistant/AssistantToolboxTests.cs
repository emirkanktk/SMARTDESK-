using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using MediatR;
using Microsoft.Extensions.Time.Testing;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.Assistant.Common;
using SmartDesk.Application.Features.Reservations.Common;
using SmartDesk.Application.Features.Reservations.Queries.GetMyReservations;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Application.Features.Resources.Queries.GetAvailableResources;
using SmartDesk.Application.Features.Resources.Queries.GetResourceById;
using SmartDesk.Application.Features.Resources.Queries.GetResources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Tests.Assistant;

// The assistant's tools (ADR-0029): they ask the same queries the screens do, as the user, and
// turn times between the user's clock and UTC. Nothing they do books or cancels anything.
public class AssistantToolboxTests
{
    // Monday 5 October 2026, 09:00 in Istanbul, which is UTC+3 all year.
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 6, 0, 0, TimeSpan.Zero);
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    private readonly Mock<ISender> _sender = new();
    private readonly AssistantToolbox _toolbox;

    public AssistantToolboxTests() => _toolbox = new AssistantToolbox(_sender.Object, Istanbul, new FakeTimeProvider(Now));

    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, DateTimeKind.Utc);

    private static ResourceDto Room(string name, int capacity = 8, string location = "2nd Floor", params string[] features) =>
        new(Guid.NewGuid(), name, Guid.NewGuid(), "Meeting Room", capacity, location, ResourceStatus.Active, null, features, [], null, false);

    private static ReservationDto Booking(ResourceDto room, DateTime start, DateTime end, ReservationStatus status = ReservationStatus.Confirmed) =>
        new(Guid.NewGuid(), room.Id, room.Name, room.ResourceTypeId, room.ResourceTypeName, room.Location, Guid.NewGuid(),
            start, end, status, "Weekly sync", null, false, null, null);

    private static async Task<JsonElement> RunAsync(AssistantToolbox toolbox, string tool, object input) =>
        JsonSerializer.Deserialize<JsonElement>(
            await toolbox.RunAsync(tool, JsonSerializer.SerializeToElement(input), CancellationToken.None));

    private Task<JsonElement> RunAsync(string tool, object input) => RunAsync(_toolbox, tool, input);

    private static string? Error(JsonElement result) =>
        result.TryGetProperty("error", out var error) ? error.GetString() : null;

    private List<GetAvailableResourcesQuery> AnswerFreeWith(params ResourceDto[] free)
    {
        List<GetAvailableResourcesQuery> asked = [];
        _sender.Setup(s => s.Send(It.IsAny<GetAvailableResourcesQuery>(), It.IsAny<CancellationToken>()))
            .Callback((IRequest<IReadOnlyList<ResourceDto>> query, CancellationToken _) => asked.Add((GetAvailableResourcesQuery)query))
            .ReturnsAsync(free);
        return asked;
    }

    private void AnswerResource(ResourceDto resource) =>
        _sender.Setup(s => s.Send(new GetResourceByIdQuery(resource.Id), It.IsAny<CancellationToken>())).ReturnsAsync(resource);

    private void AnswerMine(params ReservationDto[] mine) =>
        _sender.Setup(s => s.Send(It.IsAny<GetMyReservationsQuery>(), It.IsAny<CancellationToken>())).ReturnsAsync(mine);

    [Fact]
    public async Task FindFreeResources_ReadsLocalTimesInTheUsersZone()
    {
        var asked = AnswerFreeWith(Room("Room B", capacity: 10), Room("Room A", capacity: 4));

        var result = await RunAsync(AssistantToolbox.FindFreeResources,
            new { start = "2026-10-06T10:00", end = "2026-10-06T11:30", minCapacity = 4 });

        asked.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new GetAvailableResourcesQuery(Utc(6, 7), Utc(6, 8, 30), null, 4));
        result.GetProperty("start").GetString().Should().Be("2026-10-06T10:00");
        result.GetProperty("end").GetString().Should().Be("2026-10-06T11:30");
        // The smallest room that fits first.
        result.GetProperty("resources").EnumerateArray().Select(r => r.GetProperty("name").GetString())
            .Should().Equal("Room A", "Room B");
    }

    [Fact]
    public async Task FindFreeResources_SaysWhichRoomsAskForACheckIn()
    {
        AnswerFreeWith(Room("Room A") with { CheckInRequired = true }, Room("Room B"));

        var result = await RunAsync(AssistantToolbox.FindFreeResources, new { start = "2026-10-06T10:00", end = "2026-10-06T11:00" });

        // So the assistant can tell the user about the QR code at the door (ADR-0030).
        result.GetProperty("resources").EnumerateArray()
            .ToDictionary(r => r.GetProperty("name").GetString()!, r => r.GetProperty("checkInRequired").GetBoolean())
            .Should().Equal(new Dictionary<string, bool> { ["Room A"] = true, ["Room B"] = false });
    }

    [Fact]
    public async Task FindFreeResources_TimeWithAnOffset_IsTheInstantItNames()
    {
        var asked = AnswerFreeWith();

        await RunAsync(AssistantToolbox.FindFreeResources, new { start = "2026-10-06T07:00Z", end = "2026-10-06T09:00+01:00" });

        asked.Should().ContainSingle().Which.Should().BeEquivalentTo(new GetAvailableResourcesQuery(Utc(6, 7), Utc(6, 8)));
    }

    [Fact]
    public async Task FindFreeResources_CapacityWrittenAsText_IsRead()
    {
        var asked = AnswerFreeWith();

        await RunAsync(AssistantToolbox.FindFreeResources, new { start = "2026-10-06T10:00", end = "2026-10-06T11:00", minCapacity = "6" });

        asked.Should().ContainSingle().Which.MinCapacity.Should().Be(6);
    }

    [Theory]
    [InlineData("tomorrow at 10")]
    [InlineData("06.10.2026 10:00")]
    public async Task FindFreeResources_TimeItCantRead_TellsTheModelTheFormat(string start)
    {
        var result = await RunAsync(AssistantToolbox.FindFreeResources, new { start, end = "2026-10-06T11:00" });

        Error(result).Should().Contain("2026-10-01T14:00");
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FindFreeResources_TimeTheClocksSkip_IsAnError()
    {
        // In Berlin the clocks go from 02:00 to 03:00 on 29 March 2026.
        var berlin = new AssistantToolbox(_sender.Object, TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin"), new FakeTimeProvider(Now));

        var result = await RunAsync(berlin, AssistantToolbox.FindFreeResources, new { start = "2026-03-29T02:30", end = "2026-03-29T04:00" });

        Error(result).Should().Contain("doesn't exist");
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task FindFreeResources_QueryRefusesIt_TheModelGetsTheReason()
    {
        _sender.Setup(s => s.Send(It.IsAny<GetAvailableResourcesQuery>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ValidationException([new ValidationFailure("EndDateTime", "End must be after start.")]));

        var result = await RunAsync(AssistantToolbox.FindFreeResources, new { start = "2026-10-06T11:00", end = "2026-10-06T10:00" });

        Error(result).Should().Be("End must be after start.");
    }

    [Theory]
    [InlineData("izmir", "İzmir Room")]
    [InlineData("PROJEKSİYON", "Board Room")]
    [InlineData("3rd floor", "İzmir Room")]
    public async Task FindResources_MatchesNamePlaceOrEquipment_InEitherCase(string search, string expected)
    {
        _sender.Setup(s => s.Send(new GetResourcesQuery(null, ResourceStatus.Active), It.IsAny<CancellationToken>()))
            .ReturnsAsync([Room("İzmir Room", location: "3rd Floor"), Room("Board Room", location: "1st Floor", features: ["Projeksiyon"])]);

        var result = await RunAsync(AssistantToolbox.FindResources, new { search });

        result.GetProperty("resources").EnumerateArray().Select(r => r.GetProperty("name").GetString())
            .Should().Equal(expected);
    }

    [Fact]
    public async Task ListMyReservations_ByDefault_ConfirmedOnesFromTodayForAMonth()
    {
        var room = Room("Room A");
        var tomorrow = Booking(room, Utc(6, 7), Utc(6, 8));
        AnswerMine(
            Booking(room, Utc(4, 7), Utc(4, 8)),                                      // yesterday
            Booking(room, Utc(6, 9), Utc(6, 10), ReservationStatus.Cancelled),
            tomorrow,
            Booking(room, Utc(5, 7).AddDays(40), Utc(5, 8).AddDays(40)));            // after the month

        var result = await RunAsync(AssistantToolbox.ListMyReservations, new { });

        result.GetProperty("from").GetString().Should().Be("2026-10-05");
        result.GetProperty("to").GetString().Should().Be("2026-11-04");
        var listed = result.GetProperty("reservations").EnumerateArray().Should().ContainSingle().Subject;
        listed.GetProperty("id").GetGuid().Should().Be(tomorrow.Id);
        listed.GetProperty("start").GetString().Should().Be("2026-10-06T10:00");
        listed.GetProperty("end").GetString().Should().Be("2026-10-06T11:00");
    }

    [Fact]
    public async Task ProposeBooking_FreeResource_AddsACardAndBooksNothing()
    {
        var room = Room("Room A");
        AnswerResource(room);
        AnswerFreeWith(room);

        var result = await RunAsync(AssistantToolbox.ProposeBooking,
            new { resourceId = room.Id, start = "2026-10-06T10:00", end = "2026-10-06T11:00", note = "  Sprint planning " });

        result.GetProperty("proposed").GetBoolean().Should().BeTrue();
        _toolbox.Proposals.Should().ContainSingle().Which.Should().Be(new AssistantProposalDto(
            AssistantProposalDto.Book, room.Id, null, "Room A", Utc(6, 7), Utc(6, 8), "Sprint planning"));
        // Only lookups: every request the toolbox sent was a query.
        _sender.Invocations.Select(i => i.Arguments[0]).Should().AllSatisfy(request =>
            request.GetType().Name.Should().EndWith("Query"));
    }

    [Fact]
    public async Task ProposeBooking_ResourceNotFree_IsAnErrorAndAddsNoCard()
    {
        var room = Room("Room A");
        AnswerResource(room);
        AnswerFreeWith(Room("Room B"));

        var result = await RunAsync(AssistantToolbox.ProposeBooking,
            new { resourceId = room.Id, start = "2026-10-06T10:00", end = "2026-10-06T11:00" });

        Error(result).Should().StartWith("Room A isn't free for all of 2026-10-06T10:00-2026-10-06T11:00");
        _toolbox.Proposals.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeBooking_ResourceTheUserMayNotUse_IsAnErrorAndAddsNoCard()
    {
        var id = Guid.NewGuid();
        _sender.Setup(s => s.Send(new GetResourceByIdQuery(id), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotFoundException("Resource", id));

        var result = await RunAsync(AssistantToolbox.ProposeBooking,
            new { resourceId = id, start = "2026-10-06T10:00", end = "2026-10-06T11:00" });

        Error(result).Should().Contain(id.ToString());
        _toolbox.Proposals.Should().BeEmpty();
    }

    [Fact]
    public async Task ProposeBooking_StartThatHasPassed_IsAnError()
    {
        // 08:00 in Istanbul is an hour ago.
        var result = await RunAsync(AssistantToolbox.ProposeBooking,
            new { resourceId = Guid.NewGuid(), start = "2026-10-05T08:00", end = "2026-10-05T10:00" });

        Error(result).Should().Be("That start time has already passed.");
        _sender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ProposeBooking_MoreCardsThanOneReplyShows_IsRefused()
    {
        var room = Room("Room A");
        AnswerResource(room);
        AnswerFreeWith(room);
        for (var hour = 10; hour < 10 + AssistantToolbox.MaxProposals; hour++)
        {
            await RunAsync(AssistantToolbox.ProposeBooking,
                new { resourceId = room.Id, start = $"2026-10-06T{hour}:00", end = $"2026-10-06T{hour + 1}:00" });
        }

        var result = await RunAsync(AssistantToolbox.ProposeBooking,
            new { resourceId = room.Id, start = "2026-10-06T15:00", end = "2026-10-06T16:00" });

        Error(result).Should().StartWith($"At most {AssistantToolbox.MaxProposals} cards");
        _toolbox.Proposals.Should().HaveCount(AssistantToolbox.MaxProposals);
    }

    [Fact]
    public async Task ProposeCancellation_OwnUpcomingBooking_AddsACard()
    {
        var booking = Booking(Room("Room A"), Utc(6, 7), Utc(6, 8));
        AnswerMine(booking);

        var result = await RunAsync(AssistantToolbox.ProposeCancellation, new { reservationId = booking.Id });

        result.GetProperty("proposed").GetBoolean().Should().BeTrue();
        _toolbox.Proposals.Should().ContainSingle().Which.Should().Be(new AssistantProposalDto(
            AssistantProposalDto.Cancel, booking.ResourceId, booking.Id, "Room A", Utc(6, 7), Utc(6, 8), "Weekly sync"));
    }

    [Fact]
    public async Task ProposeCancellation_BookingThatIsOverOrCancelledOrNotTheirs_IsRefused()
    {
        var room = Room("Room A");
        var over = Booking(room, Utc(4, 7), Utc(4, 8));
        var cancelled = Booking(room, Utc(6, 7), Utc(6, 8), ReservationStatus.Cancelled);
        AnswerMine(over, cancelled);

        foreach (var id in new[] { over.Id, cancelled.Id, Guid.NewGuid() })
        {
            var result = await RunAsync(AssistantToolbox.ProposeCancellation, new { reservationId = id });
            Error(result).Should().NotBeNull();
        }

        _toolbox.Proposals.Should().BeEmpty();
    }

    [Fact]
    public async Task UnknownTool_IsAnError()
    {
        var result = await RunAsync("book_everything", new { });

        Error(result).Should().Be("There is no tool called book_everything.");
    }
}
