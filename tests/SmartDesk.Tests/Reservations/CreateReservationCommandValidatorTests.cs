using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;

namespace SmartDesk.Tests.Reservations;

public class CreateReservationCommandValidatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTime NowUtc = Now.UtcDateTime;

    private readonly CreateReservationCommandValidator _validator = new(new FakeTimeProvider(Now));

    private IReadOnlyList<string> Errors(DateTime start, DateTime end, string? note = null) =>
        _validator.Validate(new CreateReservationCommand(Guid.NewGuid(), start, end, note))
            .Errors.Select(e => e.PropertyName).ToList();

    [Fact]
    public void Validate_FutureRange_Passes() =>
        Errors(NowUtc.AddHours(1), NowUtc.AddHours(2)).Should().BeEmpty();

    [Fact]
    public void Validate_StartInThePast_Fails() =>
        Errors(NowUtc.AddHours(-1), NowUtc.AddHours(1)).Should().Equal(nameof(CreateReservationCommand.StartDateTime));

    [Fact]
    public void Validate_StartWithinGracePeriod_Passes() =>
        Errors(NowUtc.AddMinutes(-4), NowUtc.AddHours(1)).Should().BeEmpty();

    [Theory]
    [InlineData(0)]   // end == start
    [InlineData(-30)] // end before start
    public void Validate_EndNotAfterStart_Fails(int endOffsetMinutes) =>
        Errors(NowUtc.AddHours(1), NowUtc.AddHours(1).AddMinutes(endOffsetMinutes))
            .Should().Equal(nameof(CreateReservationCommand.EndDateTime));

    [Fact]
    public void Validate_LongerThan31Days_Fails() =>
        Errors(NowUtc.AddHours(1), NowUtc.AddDays(32)).Should().Equal(nameof(CreateReservationCommand.EndDateTime));

    [Fact]
    public void Validate_NoteTooLong_Fails() =>
        Errors(NowUtc.AddHours(1), NowUtc.AddHours(2), new string('x', 501))
            .Should().Equal(nameof(CreateReservationCommand.Note));

    [Fact]
    public void Validate_EmptyResourceId_Fails() =>
        _validator.Validate(new CreateReservationCommand(Guid.Empty, NowUtc.AddHours(1), NowUtc.AddHours(2), null))
            .Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateReservationCommand.ResourceId));

    [Theory]
    [InlineData(null)]
    [InlineData(5)]
    [InlineData(15)]
    [InlineData(30)]
    [InlineData(60)]
    public void Validate_OfferedReminder_Passes(int? minutes) =>
        _validator.Validate(new CreateReservationCommand(Guid.NewGuid(), NowUtc.AddHours(1), NowUtc.AddHours(2), null, minutes))
            .IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(90)]
    [InlineData(-15)]
    public void Validate_ReminderNotOffered_Fails(int minutes) =>
        _validator.Validate(new CreateReservationCommand(Guid.NewGuid(), NowUtc.AddHours(1), NowUtc.AddHours(2), null, minutes))
            .Errors.Should().ContainSingle(e => e.PropertyName == nameof(CreateReservationCommand.ReminderMinutes));
}
