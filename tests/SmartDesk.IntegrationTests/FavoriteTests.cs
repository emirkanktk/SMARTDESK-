using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.IntegrationTests;

// Favourite resources over HTTP, on PostgreSQL (ADR-0051): starred and unstarred by the
// signed-in user alone, idempotently, and still listed, with their status, when switched off.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class FavoriteTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Someone_stars_and_unstars_resources_of_their_own_and_nobody_else_sees_them()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var ayse = await ClientAsync("employee1@smartdesk.local");
        var mehmet = await ClientAsync("employee2@smartdesk.local");
        var room = await AddRoomAsync(admin);

        (await ayse.PutAsync($"/api/favorites/{room}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ayse.PutAsync($"/api/favorites/{room}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var favorites = (await ayse.GetFromJsonAsync<List<Room>>("/api/favorites"))!;
        favorites.Should().ContainSingle(r => r.Id == room);
        (await mehmet.GetFromJsonAsync<List<Room>>("/api/favorites"))!.Should().NotContain(r => r.Id == room);

        (await ayse.DeleteAsync($"/api/favorites/{room}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ayse.DeleteAsync($"/api/favorites/{room}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ayse.GetFromJsonAsync<List<Room>>("/api/favorites"))!.Should().NotContain(r => r.Id == room);

        (await ayse.PutAsync($"/api/favorites/{Guid.NewGuid()}", null)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fixture.CreateClient().GetAsync("/api/favorites")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_favourite_switched_off_stays_listed_saying_so()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var room = await AddRoomAsync(admin);
        (await admin.PutAsync($"/api/favorites/{room}", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await admin.PatchAsJsonAsync($"/api/resources/{room}/status", new { status = "Inactive" }))
            .IsSuccessStatusCode.Should().BeTrue();

        (await admin.GetFromJsonAsync<List<Room>>("/api/favorites"))!
            .Should().ContainSingle(r => r.Id == room).Which.Status.Should().Be("Inactive");
    }

    private static async Task<Guid> AddRoomAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Favourite room {Guid.NewGuid():N}"[..23],
            resourceTypeId = ResourceTypeSeedData.MeetingRoomId,
            capacity = 6,
            location = "3rd Floor",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Room>())!.Id;
    }

    private async Task<HttpClient> ClientAsync(string email)
    {
        var response = await fixture.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email, password = IdentityDataSeeder.DemoPassword });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginBody>())!;
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private sealed record LoginBody(string AccessToken);

    private sealed record Room(Guid Id, string? Status);
}
