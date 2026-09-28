using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// Team resources through the real pipeline on PostgreSQL (ADR-0022). The demo seed gives
// Meeting Room B to the demo manager's team, which both demo employees are in.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class TeamResourceTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_teams_resource_is_its_members_to_see_and_book_and_nobody_elses()
    {
        var member = await ClientAsync(await LoginAsync("employee1@smartdesk.local", IdentityDataSeeder.DemoPassword));
        var memberSees = await member.GetFromJsonAsync<List<ResourceRow>>("/api/resources");
        var room = memberSees!.Single(r => r.Name == "Meeting Room B");
        room.Teams.Should().ContainSingle(t => t.ManagerName == "Zeynep Kaya");

        // Someone outside the team: an employee the admin adds without one.
        var admin = await ClientAsync(await LoginAsync("admin@smartdesk.local", IdentityDataSeeder.DemoPassword));
        (await admin.PostAsJsonAsync("/api/users", new
        {
            firstName = "Solo",
            lastName = "Worker",
            email = "solo@integration.example",
            role = "Employee",
            password = "Temp-Pass-2026",
        })).StatusCode.Should().Be(HttpStatusCode.Created);
        var temporary = await ClientAsync(await LoginAsync("solo@integration.example", "Temp-Pass-2026"));
        var changed = await temporary.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "Temp-Pass-2026", newPassword = "Solo-Own-Pass-1" });
        var outsider = await ClientAsync((await changed.Content.ReadFromJsonAsync<LoginBody>())!);

        var outsiderSees = await outsider.GetFromJsonAsync<List<ResourceRow>>("/api/resources");
        outsiderSees!.Select(r => r.Name).Should().NotContain("Meeting Room B").And.Contain("Meeting Room A");
        (await outsider.GetAsync($"/api/resources/{room.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var start = DateTime.UtcNow.Date.AddDays(120).AddHours(9);
        var booking = new { resourceId = room.Id, startDateTime = start, endDateTime = start.AddHours(1) };
        (await outsider.PostAsJsonAsync("/api/reservations", booking)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await member.PostAsJsonAsync("/api/reservations", booking)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_manager_manages_their_teams_resource_but_not_a_shared_one()
    {
        var manager = await ClientAsync(await LoginAsync("manager@smartdesk.local", IdentityDataSeeder.DemoPassword));
        var resources = (await manager.GetFromJsonAsync<List<ResourceRow>>("/api/resources"))!;
        var teamRoom = resources.Single(r => r.Name == "Meeting Room B");
        var shared = resources.Single(r => r.Name == "Board Room");

        (await manager.PatchAsJsonAsync($"/api/resources/{teamRoom.Id}/status", new { status = "Inactive" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.PatchAsJsonAsync($"/api/resources/{teamRoom.Id}/status", new { status = "Active" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await manager.PatchAsJsonAsync($"/api/resources/{shared.Id}/status", new { status = "Inactive" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.PatchAsJsonAsync($"/api/resources/{teamRoom.Id}/teams", new { teamIds = Array.Empty<Guid>() })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<LoginBody> LoginAsync(string email, string password)
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginBody>())!;
    }

    private Task<HttpClient> ClientAsync(LoginBody login)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return Task.FromResult(client);
    }

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAt, bool PasswordChangeRequired);

    private sealed record TeamRow(Guid Id, string ManagerName);

    private sealed record ResourceRow(Guid Id, string Name, IReadOnlyList<TeamRow> Teams);
}
