using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// Accounts through the real pipeline on PostgreSQL (ADR-0020): the token check on every
// request, the role-scoped endpoints, and the account list's query on Npgsql.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AccountAccessTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_disabled_accounts_token_stops_working_at_once_and_works_again_when_enabled()
    {
        var admin = await ClientForAsync("admin@smartdesk.local");
        var employee = await ClientForAsync("employee2@smartdesk.local");
        var employeeId = (await employee.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

        (await admin.PostAsync($"/api/users/{employeeId}/disable", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await employee.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var login = await fixture.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email = "employee2@smartdesk.local", password = IdentityDataSeeder.DemoPassword });
        login.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await admin.PostAsync($"/api/users/{employeeId}/enable", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await employee.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Each_role_reaches_only_its_own_account_screens()
    {
        var admin = await ClientForAsync("admin@smartdesk.local");
        var manager = await ClientForAsync("manager@smartdesk.local");

        var employees = await admin.GetFromJsonAsync<List<Account>>("/api/users?role=Employee&search=SMARTDESK.LOCAL");
        employees!.Select(a => a.Email).Should().BeEquivalentTo("employee1@smartdesk.local", "employee2@smartdesk.local");
        employees.Should().OnlyContain(a => a.ManagerName == "Zeynep Kaya");

        var team = await manager.GetFromJsonAsync<List<Account>>("/api/team");
        team!.Select(a => a.Email).Should().BeEquivalentTo("employee1@smartdesk.local", "employee2@smartdesk.local");

        (await manager.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.GetAsync("/api/team")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private async Task<HttpClient> ClientForAsync(string email)
    {
        var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = IdentityDataSeeder.DemoPassword });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginBody>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    private sealed record Me(Guid Id);

    private sealed record Account(Guid Id, string Email, string Role, string Status, string? ManagerName);

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAt);
}
