using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// Temporary passwords through the real pipeline on PostgreSQL (ADR-0021): the gate that stops
// everything but a password change, and sessions that end when the password changes.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PasswordChangeTests(PostgresApiFixture fixture)
{
    private const string Temporary = "Temp-Pass-2026";

    [Fact]
    public async Task A_new_account_can_only_change_its_password_until_it_does()
    {
        var admin = await ClientAsync(await LoginAsync("admin@smartdesk.local", IdentityDataSeeder.DemoPassword));
        var created = await admin.PostAsJsonAsync("/api/users", new
        {
            firstName = "Gate",
            lastName = "Keeper",
            email = "gate@integration.example",
            role = "Employee",
            password = Temporary,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var first = await LoginAsync("gate@integration.example", Temporary);
        first.PasswordChangeRequired.Should().BeTrue();
        var temporary = await ClientAsync(first);

        var blocked = await temporary.GetAsync("/api/resources");
        blocked.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var problem = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync()))
        {
            problem.RootElement.GetProperty("code").GetString().Should().Be("password-change-required");
        }

        (await temporary.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);

        var change = await temporary.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = Temporary, newPassword = "Gates-Own-Pass-1" });
        change.StatusCode.Should().Be(HttpStatusCode.OK);
        var changed = (await change.Content.ReadFromJsonAsync<LoginBody>())!;
        changed.PasswordChangeRequired.Should().BeFalse();

        (await temporary.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "the password it was issued under is gone");
        var own = await ClientAsync(changed);
        (await own.GetAsync("/api/resources")).StatusCode.Should().Be(HttpStatusCode.OK);

        // An admin setting a temporary password ends that session too.
        var me = (await own.GetFromJsonAsync<Me>("/api/auth/me"))!;
        (await admin.PostAsJsonAsync($"/api/users/{me.Id}/password", new { password = "Another-Temp-9" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await own.GetAsync("/api/resources")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync("gate@integration.example", "Another-Temp-9")).PasswordChangeRequired.Should().BeTrue();
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

    private sealed record Me(Guid Id);
}
