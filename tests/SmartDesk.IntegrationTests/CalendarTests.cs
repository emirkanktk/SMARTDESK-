using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.IntegrationTests;

// Bookings in a calendar app, through the real pipeline on PostgreSQL (ADR-0048): one as an .ics
// file, and the personal link a calendar app fetches without a sign-in.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CalendarTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_booking_downloads_as_a_file_and_a_calendar_app_fetches_the_personal_link()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var employee = await ClientAsync("employee2@smartdesk.local");
        var room = await AddRoomAsync(admin);
        var booking = await BookAsync(employee, room.Id);

        var file = await employee.GetAsync($"/api/reservations/{booking.Id}/calendar");
        file.StatusCode.Should().Be(HttpStatusCode.OK);
        file.Content.Headers.ContentType!.MediaType.Should().Be("text/calendar");
        file.Content.Headers.ContentDisposition!.FileName.Should().EndWith(".ics");
        (await file.Content.ReadAsStringAsync()).Should().Contain($"UID:{booking.Id}@smartdesk");
        (await admin.GetAsync($"/api/reservations/{booking.Id}/calendar")).StatusCode
            .Should().Be(HttpStatusCode.NotFound, "it goes in the calendars of its own people only");

        var made = await employee.PostAsync("/api/calendar/subscription", null);
        made.StatusCode.Should().Be(HttpStatusCode.OK);
        var link = (await made.Content.ReadFromJsonAsync<SubscriptionBody>())!;
        link.Url.Should().MatchRegex(@"^http://localhost:4200/api/calendar/feed\.ics\?token=[0-9a-f]{64}$");
        var feedPath = new Uri(link.Url!).PathAndQuery;

        var calendarApp = fixture.CreateClient();
        var feed = await calendarApp.GetAsync(feedPath);
        feed.StatusCode.Should().Be(HttpStatusCode.OK);
        feed.Content.Headers.ContentType!.ToString().Should().Be("text/calendar; charset=utf-8");
        feed.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await feed.Content.ReadAsStringAsync()).Should()
            .Contain($"UID:{booking.Id}@smartdesk")
            .And.Contain("REFRESH-INTERVAL;VALUE=DURATION:PT1H");

        var status = (await employee.GetFromJsonAsync<SubscriptionBody>("/api/calendar/subscription"))!;
        status.Url.Should().BeNull("the link is shown once");
        status.LastFetchedAt.Should().NotBeNull();

        (await employee.DeleteAsync("/api/calendar/subscription")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await calendarApp.GetAsync(feedPath)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await employee.GetAsync("/api/calendar/subscription")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    private static async Task<ResourceRow> AddRoomAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Calendar room {Guid.NewGuid():N}"[..22],
            resourceTypeId = ResourceTypeSeedData.MeetingRoomId,
            capacity = 4,
            location = "1st Floor",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ResourceRow>())!;
    }

    // Tomorrow, 09:00 UTC: noon in the office's Istanbul.
    private static async Task<ReservationBody> BookAsync(HttpClient client, Guid roomId)
    {
        var start = DateTime.UtcNow.Date.AddDays(1).AddHours(9);
        var response = await client.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = roomId,
            startDateTime = start,
            endDateTime = start.AddHours(1),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ReservationBody>())!;
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

    private sealed record ResourceRow(Guid Id, string Name);

    private sealed record ReservationBody(Guid Id);

    private sealed record SubscriptionBody(string? Url, DateTime CreatedAt, DateTime? LastFetchedAt);
}
