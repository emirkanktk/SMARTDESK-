using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// Announcements (ADR-0065) through the real pipeline on PostgreSQL: an Admin posts one, everyone
// reads it while it shows, and only Admins post or take one down.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AnnouncementTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task An_admin_posts_one_everyone_reads_it_and_taken_down_it_is_gone()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var employee = await ClientAsync("employee1@smartdesk.local");
        var title = $"Breakfast {Guid.NewGuid():N}"[..20];

        var posted = await admin.PostAsJsonAsync("/api/announcements", new
        {
            title,
            body = "Croissants on the 5th floor.",
            tone = "Good",
            endsAt = DateTime.UtcNow.AddDays(2),
            notify = false,
        });

        posted.StatusCode.Should().Be(HttpStatusCode.Created);
        var announcement = (await posted.Content.ReadFromJsonAsync<AnnouncementBody>())!;
        announcement.Should().Match<AnnouncementBody>(a => a.Title == title && a.Tone == "Good" && a.AuthorName == "System Admin");
        (await employee.GetFromJsonAsync<List<AnnouncementBody>>("/api/announcements"))!
            .Should().Contain(a => a.Id == announcement.Id);

        (await admin.DeleteAsync($"/api/announcements/{announcement.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await employee.GetFromJsonAsync<List<AnnouncementBody>>("/api/announcements"))!
            .Should().NotContain(a => a.Id == announcement.Id);
    }

    [Fact]
    public async Task Only_an_admin_posts_or_lists_them_all_and_an_end_gone_by_is_refused()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var employee = await ClientAsync("employee1@smartdesk.local");
        var body = new { title = "Mine", tone = "Info", endsAt = DateTime.UtcNow.AddDays(1), notify = false };

        (await employee.PostAsJsonAsync("/api/announcements", body)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await employee.GetAsync("/api/announcements/all")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fixture.CreateClient().GetAsync("/api/announcements")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var past = await admin.PostAsJsonAsync("/api/announcements", body with { endsAt = DateTime.UtcNow.AddHours(-1) });
        past.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await past.Content.ReadAsStringAsync()).Should().Contain("EndsAt");
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

    private sealed record AnnouncementBody(
        Guid Id, string Title, string? Body, string Tone, DateTime CreatedAt, DateTime EndsAt, Guid? AuthorId, string? AuthorName);
}
