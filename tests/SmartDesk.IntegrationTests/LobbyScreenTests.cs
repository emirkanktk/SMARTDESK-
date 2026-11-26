using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// Lobby screens (ADR-0064) through the real pipeline on PostgreSQL: an Admin sets one up and gets
// its link once; the screen, with nobody signed in, shows the office with the token from it; taken
// down, it shows nothing.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LobbyScreenTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task An_admin_sets_one_up_the_screen_shows_the_office_and_taken_down_it_shows_nothing()
    {
        var admin = await ClientAsync("admin@smartdesk.local");

        var created = await admin.PostAsJsonAsync("/api/lobby-screens", new { name = $"Entrance {Guid.NewGuid():N}"[..20] });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        created.Headers.CacheControl!.NoStore.Should().BeTrue("the answer holds the screen's token");
        var screen = (await created.Content.ReadFromJsonAsync<NewScreenBody>())!;
        screen.Link.Should().MatchRegex("^http://localhost:4200/lobby#[0-9a-f]{64}$");
        var token = screen.Link[(screen.Link.IndexOf('#') + 1)..];

        var lobby = await ScreenAsync(token).GetAsync("/api/lobby?timeZone=Europe/Istanbul");
        lobby.StatusCode.Should().Be(HttpStatusCode.OK);
        var shown = (await lobby.Content.ReadFromJsonAsync<LobbyBody>())!;
        shown.ScreenName.Should().Be(screen.Screen.Name);
        shown.Rooms.Should().Contain(r => r.Name == "Board Room", "the seeded meeting rooms are in the office");
        shown.AppUrl.Should().Be("http://localhost:4200/");
        (await admin.GetFromJsonAsync<List<ScreenBody>>("/api/lobby-screens"))!
            .Should().Contain(s => s.Id == screen.Screen.Id && s.LastSeenAt != null, "it has asked");

        (await admin.DeleteAsync($"/api/lobby-screens/{screen.Screen.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ScreenAsync(token).GetAsync("/api/lobby")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_an_admin_manages_them_and_a_screen_without_its_token_gets_nothing()
    {
        var employee = await ClientAsync("employee1@smartdesk.local");

        (await employee.GetAsync("/api/lobby-screens")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await employee.PostAsJsonAsync("/api/lobby-screens", new { name = "Mine" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fixture.CreateClient().GetAsync("/api/lobby-screens")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await fixture.CreateClient().GetAsync("/api/lobby")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await ScreenAsync(new string('a', 64)).GetAsync("/api/lobby")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    private HttpClient ScreenAsync(string token)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Add("X-Lobby-Token", token);
        return client;
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

    private sealed record ScreenBody(Guid Id, string Name, string? Location, DateTime CreatedAt, DateTime? LastSeenAt);

    private sealed record NewScreenBody(ScreenBody Screen, string Link);

    private sealed record RoomBody(Guid Id, string Name, string Location, int Capacity, string Status);

    private sealed record DesksBody(string Location, int Free, int Total);

    private sealed record LobbyBody(
        string ScreenName, string? Location, DateTime Now, string AppUrl, int PeopleToday, List<RoomBody> Rooms, List<DesksBody> Desks);
}
