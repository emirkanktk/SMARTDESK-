using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// PUT /api/auth/me over HTTP: a user changes their own name, and the rules and the temporary
// password gate apply as everywhere else.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ProfileTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_user_changes_their_name_and_everyone_sees_it()
    {
        var admin = Bearer((await LoginAsync("admin@smartdesk.local", IdentityDataSeeder.DemoPassword)).AccessToken);
        (await admin.PostAsJsonAsync("/api/users", new
        {
            firstName = "Profil",
            lastName = "Deneme",
            email = "profile@integration.example",
            role = "Employee",
            password = "Temp-Pass-2026",
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        // A temporary password comes first: nothing but changing it is allowed until then.
        var temporary = Bearer((await LoginAsync("profile@integration.example", "Temp-Pass-2026")).AccessToken);
        (await temporary.PutAsJsonAsync("/api/auth/me", new { firstName = "Early", lastName = "Rename" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var changed = await temporary.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "Temp-Pass-2026", newPassword = "Profile-Own-Pass-1" });
        var user = Bearer((await changed.Content.ReadFromJsonAsync<LoginBody>())!.AccessToken);

        var renamed = await user.PutAsJsonAsync("/api/auth/me", new { firstName = " Profil Nur ", lastName = "Demir" });
        renamed.StatusCode.Should().Be(HttpStatusCode.OK);
        var profile = (await renamed.Content.ReadFromJsonAsync<Profile>())!;
        profile.Should().Be(new Profile("Profil Nur", "Demir", "profile@integration.example"));
        (await user.GetFromJsonAsync<Profile>("/api/auth/me")).Should().Be(profile);

        var list = await admin.GetFromJsonAsync<List<Account>>("/api/users");
        list!.Single(a => a.Email == "profile@integration.example").DisplayName.Should().Be("Profil Nur Demir");
    }

    [Fact]
    public async Task A_name_follows_the_sign_up_rules()
    {
        var user = Bearer((await LoginAsync("employee2@smartdesk.local", IdentityDataSeeder.DemoPassword)).AccessToken);

        var refused = await user.PutAsJsonAsync("/api/auth/me", new { firstName = "", lastName = "Demir" });

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadAsStringAsync()).Should().Contain("FirstName");
    }

    private async Task<LoginBody> LoginAsync(string email, string password)
    {
        var response = await fixture.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginBody>())!;
    }

    private HttpClient Bearer(string token)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private sealed record LoginBody(string AccessToken);

    private sealed record Profile(string FirstName, string LastName, string Email);

    private sealed record Account(string Email, string DisplayName);
}
