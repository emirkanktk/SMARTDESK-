using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.BookingRules.Commands.UpdateOfficeHours;
using SmartDesk.Application.Features.BookingRules.Commands.UpdateResourceTypeRules;
using SmartDesk.Application.Features.BookingRules.Common;
using SmartDesk.Application.Features.BookingRules.Queries.GetBookingRules;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.BookingRules;

// Reading the rules, and an Admin setting them (ADR-0046).
public sealed class BookingRulesHandlerTests : IDisposable
{
    private static readonly DayOfWeek[] Weekdays =
        [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday];

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    [Fact]
    public async Task AsTheMigrationLeavesThem_TheOfficeWorksAWeekAndNoKindHasRulesOfItsOwn()
    {
        await using var context = _database.CreateContext();

        var rules = await new GetBookingRulesQueryHandler(context).Handle(new GetBookingRulesQuery(), CancellationToken.None);

        rules.OfficeHours.TimeZone.Should().Be("Europe/Istanbul");
        rules.OfficeHours.OpensAt.Should().Be(new TimeOnly(9, 0));
        rules.OfficeHours.ClosesAt.Should().Be(new TimeOnly(18, 0));
        rules.OfficeHours.Workdays.Should().Equal(Weekdays);
        rules.OfficeHours.Description.Should().Be("Mon-Fri 09:00-18:00");
        rules.Types.Select(t => t.Name).Should().Equal("Camera", "Company Car", "Desk", "Meeting Room", "Projector");
        rules.Types.Should().OnlyContain(t =>
            t.MaxDurationMinutes == null && t.MaxDaysAhead == null && t.MaxActivePerUser == null && !t.OfficeHoursOnly);
        rules.Bounds.MaxDurationMinutes.Should().Be(31 * 24 * 60);
        rules.Bounds.MaxDaysAhead.Should().Be(180);
        rules.Bounds.MaxActivePerUser.Should().Be(100);
    }

    [Fact]
    public async Task AnAdminSetsTheOfficesHours()
    {
        using var _ = UiCulture.Use("tr-TR");
        await using (var context = _database.CreateContext())
        {
            var saved = await new UpdateOfficeHoursCommandHandler(context).Handle(
                new UpdateOfficeHoursCommand("Europe/London", new TimeOnly(8, 30), new TimeOnly(17, 15), [DayOfWeek.Saturday, .. Weekdays]),
                CancellationToken.None);

            saved.Description.Should().Be("Pzt-Cmt 08:30-17:15");
        }

        await using var read = _database.CreateContext();
        var hours = await read.OfficeHours.SingleAsync();
        hours.TimeZone.Should().Be("Europe/London");
        hours.Workdays.Should().Be(WeekDays.Monday | WeekDays.Tuesday | WeekDays.Wednesday | WeekDays.Thursday | WeekDays.Friday | WeekDays.Saturday);
    }

    [Theory]
    [InlineData("Mars/Olympus", 9, 0, 18, 0, "TimeZone")]
    [InlineData("Europe/Istanbul", 9, 10, 18, 0, "OpensAt")]
    [InlineData("Europe/Istanbul", 9, 0, 9, 0, "ClosesAt")]
    [InlineData("Europe/Istanbul", 18, 0, 9, 0, "ClosesAt")]
    public void TheHours_AreOnTheQuarterHour_AndCloseAfterTheyOpen(
        string zone, int opensHour, int opensMinute, int closesHour, int closesMinute, string property)
    {
        var result = new UpdateOfficeHoursCommandValidator().Validate(new UpdateOfficeHoursCommand(
            zone, new TimeOnly(opensHour, opensMinute), new TimeOnly(closesHour, closesMinute), Weekdays));

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(property);
    }

    [Fact]
    public void TheOfficeWorksOnAtLeastOneDay()
    {
        var result = new UpdateOfficeHoursCommandValidator().Validate(
            new UpdateOfficeHoursCommand("Europe/Istanbul", new TimeOnly(9, 0), new TimeOnly(18, 0), []));

        result.Errors.Should().ContainSingle().Which.ErrorMessage.Should().Be("Pick at least one workday.");
    }

    [Fact]
    public async Task AnAdminSetsAKindsRules_AndUnsetsThem()
    {
        await using (var context = _database.CreateContext())
        {
            var saved = await new UpdateResourceTypeRulesCommandHandler(context).Handle(
                new UpdateResourceTypeRulesCommand(540, 14, 2, true) { Id = ResourceTypeSeedData.DeskId }, CancellationToken.None);

            saved.Should().Be(new ResourceTypeRulesDto(ResourceTypeSeedData.DeskId, "Desk", 540, 14, 2, true));
        }

        await using (var context = _database.CreateContext())
        {
            await new UpdateResourceTypeRulesCommandHandler(context).Handle(
                new UpdateResourceTypeRulesCommand(null, null, null, false) { Id = ResourceTypeSeedData.DeskId }, CancellationToken.None);
        }

        await using var read = _database.CreateContext();
        var desks = await read.ResourceTypes.SingleAsync(t => t.Id == ResourceTypeSeedData.DeskId);
        desks.MaxDurationMinutes.Should().BeNull();
        desks.MaxDaysAhead.Should().BeNull();
        desks.MaxActivePerUser.Should().BeNull();
        desks.OfficeHoursOnly.Should().BeFalse();
    }

    [Fact]
    public async Task AKindThatIsntThere_IsA404()
    {
        await using var context = _database.CreateContext();

        var act = () => new UpdateResourceTypeRulesCommandHandler(context).Handle(
            new UpdateResourceTypeRulesCommand(null, null, null, false) { Id = Guid.NewGuid() }, CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData(10, null, null, "MaxDurationMinutes")]      // shorter than a quarter hour
    [InlineData(100, null, null, "MaxDurationMinutes")]     // not in quarter hours
    [InlineData(44655, null, null, "MaxDurationMinutes")]   // longer than any booking
    [InlineData(null, -1, null, "MaxDaysAhead")]
    [InlineData(null, 181, null, "MaxDaysAhead")]           // further than any booking
    [InlineData(null, null, 0, "MaxActivePerUser")]
    [InlineData(null, null, 101, "MaxActivePerUser")]
    public void AKindsRules_StayWithinTheRulesForEveryBooking(int? minutes, int? daysAhead, int? perPerson, string property)
    {
        var result = new UpdateResourceTypeRulesCommandValidator().Validate(
            new UpdateResourceTypeRulesCommand(minutes, daysAhead, perPerson, false));

        result.Errors.Should().ContainSingle().Which.PropertyName.Should().Be(property);
    }

    [Theory]
    [InlineData(15, 0, 1)]
    [InlineData(44640, 180, 100)]
    public void TheirBounds_AreAllowed(int minutes, int daysAhead, int perPerson)
    {
        new UpdateResourceTypeRulesCommandValidator()
            .Validate(new UpdateResourceTypeRulesCommand(minutes, daysAhead, perPerson, true))
            .IsValid.Should().BeTrue();
    }
}
