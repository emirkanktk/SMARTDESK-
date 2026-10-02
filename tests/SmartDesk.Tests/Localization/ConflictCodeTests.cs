using FluentAssertions;
using Moq;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Reservations.Commands.CreateReservation;
using SmartDesk.Application.Features.ResourceBlocks.Commands.CreateResourceBlock;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Enums;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Localization;

// The conflicts a screen has to tell apart carry a code, and their message follows the
// request's language (ADR-0023).
public sealed class ConflictCodeTests : IDisposable
{
    private static readonly DateTime Day = new(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();

    private readonly SqliteTestDatabase _database = new();

    public void Dispose() => _database.Dispose();

    private static DateTime At(int hour) => Day.AddHours(hour);

    private async Task BookAsync(Guid resourceId, int from, int to)
    {
        await using var context = _database.CreateContext();
        await new CreateReservationCommandHandler(context, new FakeCurrentUser(UserId), new FakeIdentityService(), Mock.Of<IReservationNotifier>(), TestInvitations.For(context, new FakeCurrentUser(UserId)))
            .Handle(new CreateReservationCommand(resourceId, At(from), At(to), null), CancellationToken.None);
    }

    private async Task BlockAsync(Guid resourceId, int from, int to)
    {
        await using var context = _database.CreateContext();
        await new CreateResourceBlockCommandHandler(context, new FakeCurrentUser(UserId, Roles.Admin))
            .Handle(new CreateResourceBlockCommand(resourceId, At(from), At(to), "Desk repair"), CancellationToken.None);
    }

    [Fact]
    public async Task A_resource_in_maintenance()
    {
        var room = await _database.AddResourceAsync("Meeting Room A", status: ResourceStatus.Maintenance);
        using var turkish = UiCulture.Use("tr");

        var act = () => BookAsync(room.Id, 10, 11);

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Should().Match<ConflictException>(e =>
                e.Code == ConflictCodes.ResourceUnavailable && e.Message == "Meeting Room A şu anda rezerve edilemez (durum: Bakımda).");
    }

    [Fact]
    public async Task A_blocked_window()
    {
        var desk = await _database.AddResourceAsync("Desk 3-01");
        await _database.AddBlockAsync(desk.Id, At(9), At(12), "Desk repair");
        using var turkish = UiCulture.Use("tr");

        var act = () => BookAsync(desk.Id, 10, 11);

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Should().Match<ConflictException>(e =>
                e.Code == ConflictCodes.ResourceBlocked && e.Message == "Desk 3-01 bu sürenin bir kısmında kapalı: Desk repair");
    }

    [Fact]
    public async Task A_slot_someone_else_has()
    {
        var room = await _database.AddResourceAsync("Meeting Room A");
        await _database.AddReservationAsync(room.Id, At(10), At(11));

        var act = () => BookAsync(room.Id, 10, 11);

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Should().Match<ConflictException>(e =>
                e.Code == ConflictCodes.ReservationOverlaps && e.Message == "Meeting Room A is already reserved for part of that time.");
    }

    [Fact]
    public async Task A_block_over_another_block()
    {
        var desk = await _database.AddResourceAsync("Desk 3-01");
        await _database.AddBlockAsync(desk.Id, At(9), At(12));

        var act = () => BlockAsync(desk.Id, 10, 14);

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Code.Should().Be(ConflictCodes.BlockOverlapsBlock);
    }

    [Fact]
    public async Task A_block_over_reservations()
    {
        var desk = await _database.AddResourceAsync("Desk 3-01");
        await _database.AddReservationAsync(desk.Id, At(10), At(11));
        using var turkish = UiCulture.Use("tr");

        var act = () => BlockAsync(desk.Id, 9, 12);

        (await act.Should().ThrowAsync<ConflictException>())
            .Which.Should().Match<ConflictException>(e =>
                e.Code == ConflictCodes.BlockOverlapsReservations
                && e.Message == "Desk 3-01 için 1 onaylı rezervasyon bu aralıkla çakışıyor. Önce onları iptal edin ya da taşıyın.");
    }
}
