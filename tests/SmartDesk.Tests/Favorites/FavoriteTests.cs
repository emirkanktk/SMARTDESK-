using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.Favorites.Commands.AddFavorite;
using SmartDesk.Application.Features.Favorites.Commands.RemoveFavorite;
using SmartDesk.Application.Features.Favorites.Queries.GetFavorites;
using SmartDesk.Application.Features.Resources.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Domain.Rules;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Favorites;

// Favourite resources (ADR-0051): each person's own short list, starred and unstarred at will,
// of what they may use.
public sealed class FavoriteTests : IDisposable
{
    private static readonly DateTime Ten = new(2026, 10, 16, 10, 0, 0, DateTimeKind.Utc);

    // Selin runs a team Ayşe is in; Mehmet is in no team.
    private static readonly Guid SelinId = Guid.NewGuid();
    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee) { TeamId = SelinId };
    private static readonly FakeCurrentUser Mehmet = new(Guid.NewGuid(), Roles.Employee);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Ten));

    public void Dispose() => _database.Dispose();

    private async Task<Resource> AddRoomAsync(string name, Guid? team = null, ResourceStatus status = ResourceStatus.Active)
    {
        var room = await _database.AddResourceAsync(name, status: status);
        if (team is { } teamId)
        {
            await using var context = _database.CreateContext();
            context.ResourceTeams.Add(new ResourceTeam { ResourceId = room.Id, TeamId = teamId });
            await context.SaveChangesAsync();
        }

        return room;
    }

    private async Task StarAsync(Guid resourceId, FakeCurrentUser? user = null)
    {
        await using var context = _database.CreateContext();
        await new AddFavoriteCommandHandler(context, user ?? Ayse, _clock)
            .Handle(new AddFavoriteCommand(resourceId), CancellationToken.None);
    }

    private async Task UnstarAsync(Guid resourceId, FakeCurrentUser? user = null)
    {
        await using var context = _database.CreateContext();
        await new RemoveFavoriteCommandHandler(context, user ?? Ayse)
            .Handle(new RemoveFavoriteCommand(resourceId), CancellationToken.None);
    }

    private async Task<IReadOnlyList<ResourceDto>> FavoritesAsync(FakeCurrentUser? user = null)
    {
        await using var context = _database.CreateContext();
        return await new GetFavoritesQueryHandler(context, user ?? Ayse, new ResourceReader(new FakeIdentityService()))
            .Handle(new GetFavoritesQuery(), CancellationToken.None);
    }

    [Fact]
    public async Task Starred_resources_are_the_callers_own_by_name_and_starring_again_changes_nothing()
    {
        var board = await AddRoomAsync("Board Room");
        var desk = await AddRoomAsync("Desk 3-01");
        await AddRoomAsync("Meeting Room B");

        await StarAsync(desk.Id);
        await StarAsync(board.Id);
        await StarAsync(board.Id);

        (await FavoritesAsync()).Select(r => r.Name).Should().Equal("Board Room", "Desk 3-01");
        (await FavoritesAsync(Mehmet)).Should().BeEmpty("they're Ayşe's");
        await using var context = _database.CreateContext();
        (await context.FavoriteResources.CountAsync()).Should().Be(2);
        (await context.FavoriteResources.FirstAsync()).CreatedAt.Should().Be(Ten);
    }

    [Fact]
    public async Task Unstarring_takes_it_off_and_unstarring_again_changes_nothing()
    {
        var board = await AddRoomAsync("Board Room");
        await StarAsync(board.Id);

        await UnstarAsync(board.Id);
        await UnstarAsync(board.Id);

        (await FavoritesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Only_what_the_caller_may_use_is_starred_or_listed()
    {
        var teams = await AddRoomAsync("Selin's Room", team: SelinId);
        var others = await AddRoomAsync("Kerem's Room", team: Guid.NewGuid());

        await StarAsync(teams.Id);
        await FluentActions.Awaiting(() => StarAsync(others.Id)).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => StarAsync(Guid.NewGuid())).Should().ThrowAsync<NotFoundException>();
        await FluentActions.Awaiting(() => StarAsync(teams.Id, Mehmet)).Should().ThrowAsync<NotFoundException>();

        // Moved to another team: kept, but out of sight until it's hers to use again.
        await using (var context = _database.CreateContext())
        {
            var team = await context.ResourceTeams.SingleAsync(t => t.ResourceId == teams.Id);
            context.ResourceTeams.Remove(team);
            context.ResourceTeams.Add(new ResourceTeam { ResourceId = teams.Id, TeamId = Guid.NewGuid() });
            await context.SaveChangesAsync();
        }

        (await FavoritesAsync()).Should().BeEmpty();
        await using var read = _database.CreateContext();
        (await read.FavoriteResources.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task A_favourite_out_of_use_stays_listed_saying_so_and_goes_with_its_resource()
    {
        var camera = await AddRoomAsync("Camera C-1", status: ResourceStatus.Maintenance);
        await StarAsync(camera.Id);

        (await FavoritesAsync()).Should().ContainSingle().Which.Status.Should().Be(ResourceStatus.Maintenance);

        await using (var context = _database.CreateContext())
        {
            context.Resources.Remove(await context.Resources.SingleAsync(r => r.Id == camera.Id));
            await context.SaveChangesAsync();
        }

        await using var read = _database.CreateContext();
        (await read.FavoriteResources.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task One_keeps_only_so_many()
    {
        for (var i = 0; i < FavoriteRules.MaxPerUser; i++)
        {
            await StarAsync((await AddRoomAsync($"Desk {i:00}")).Id);
        }

        var oneMore = await AddRoomAsync("Desk 99");
        var refused = await FluentActions.Awaiting(() => StarAsync(oneMore.Id)).Should().ThrowAsync<ConflictException>();
        refused.Which.Code.Should().Be(ConflictCodes.FavoritesFull);
        refused.WithMessage($"*{FavoriteRules.MaxPerUser}*");

        // One already starred is no more.
        await StarAsync((await FavoritesAsync())[0].Id);
        await StarAsync(oneMore.Id, Mehmet);
    }

    [Fact]
    public async Task Nobody_signed_in_has_no_favourites()
    {
        var board = await AddRoomAsync("Board Room");
        var nobody = new FakeCurrentUser(null);

        await FluentActions.Awaiting(() => FavoritesAsync(nobody)).Should().ThrowAsync<UnauthorizedException>();
        await FluentActions.Awaiting(() => StarAsync(board.Id, nobody)).Should().ThrowAsync<UnauthorizedException>();
        await FluentActions.Awaiting(() => UnstarAsync(board.Id, nobody)).Should().ThrowAsync<UnauthorizedException>();
    }
}
