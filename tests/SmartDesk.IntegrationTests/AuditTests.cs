using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.IntegrationTests;

// The history of changes over HTTP, on PostgreSQL (ADR-0052): what the API's own saves wrote,
// who it came from - someone signed in, or someone at the door without an account - and that
// only Admins read it.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AuditTests(PostgresApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task A_resources_history_says_who_made_it_booked_it_cancelled_it_and_checked_in_at_the_door()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var ayse = await ClientAsync("employee1@smartdesk.local");
        var created = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"History room {Guid.NewGuid():N}"[..21],
            resourceTypeId = ResourceTypeSeedData.MeetingRoomId,
            capacity = 6,
            location = "4th Floor",
            checkInRequired = true,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var room = (await created.Content.ReadFromJsonAsync<Room>())!.Id;
        var code = (await admin.GetFromJsonAsync<PrintedCode>($"/api/resources/{room}/check-in-code"))!.Code;

        var later = await ayse.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room,
            startDateTime = DateTime.UtcNow.AddDays(1),
            endDateTime = DateTime.UtcNow.AddDays(1).AddHours(1),
        });
        var booking = (await later.Content.ReadFromJsonAsync<Booking>())!.Id;
        (await ayse.PostAsync($"/api/reservations/{booking}/cancel", null)).IsSuccessStatusCode.Should().BeTrue();
        var now = await ayse.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room,
            startDateTime = DateTime.UtcNow.AddMinutes(-1),
            endDateTime = DateTime.UtcNow.AddMinutes(59),
        });
        now.StatusCode.Should().Be(HttpStatusCode.Created);
        (await fixture.CreateClient().PostAsJsonAsync($"/api/door/{room}/check-in", new { code }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var page = (await admin.GetFromJsonAsync<Page>($"/api/audit?resourceId={room}", Json))!;

        var oldestFirst = page.Items.AsEnumerable().Reverse().ToList();
        oldestFirst.Select(e => (e.EntityType, e.Action, e.Actor)).Should().Equal(
            ("Resource", "Created", "User"),
            ("Reservation", "Created", "User"),
            ("Reservation", "Updated", "User"),
            ("Reservation", "Created", "User"),
            ("Reservation", "Updated", "Anonymous"));
        var adminId = oldestFirst[0].UserId!.Value;
        page.People[adminId].Should().NotBeNullOrWhiteSpace();
        page.Resources[room].Should().StartWith("History room");
        oldestFirst[2].Changes.Should().ContainEquivalentOf(new Change("Status", "Confirmed", "Cancelled"));
        oldestFirst[4].UserId.Should().BeNull();
        oldestFirst[4].Changes.Select(c => c.Property).Should().Contain("CheckedInAt");
        oldestFirst[0].Changes.Should().ContainEquivalentOf(new Change("CheckInCode", null, "***"));
        JsonSerializer.Serialize(page).Should().NotContain(code, "a secret is kept only as whether there is one");

        // Only for Admins; and an area it doesn't know is refused, saying which there are.
        (await ayse.GetAsync("/api/audit")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var unknown = await admin.GetAsync("/api/audit?area=everything");
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await unknown.Content.ReadAsStringAsync()).Should().Contain("bookings, resources, rules, problems or accounts");
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

    private sealed record Room(Guid Id);

    private sealed record PrintedCode(string Code);

    private sealed record Booking(Guid Id);

    private sealed record Change(string Property, string? Old, string? New);

    private sealed record Entry(
        long Id, string Actor, Guid? UserId, string EntityType, string EntityId, string Action, Guid? ResourceId, List<Change> Changes);

    private sealed record Page(List<Entry> Items, bool HasMore, Dictionary<Guid, string> People, Dictionary<Guid, string> Resources);
}
