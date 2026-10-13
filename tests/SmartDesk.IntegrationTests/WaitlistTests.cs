using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.IntegrationTests;

// The waiting list over HTTP, on PostgreSQL (ADR-0043): the saves that free a time, the
// pipeline that offers it, and the booking made for whoever waited, wired as the app runs.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class WaitlistTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_cancelled_booking_is_booked_for_whoever_was_waiting_and_they_are_told()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var ayse = await ClientAsync("employee1@smartdesk.local");
        var mehmet = await ClientAsync("employee2@smartdesk.local");
        var room = await AddRoomAsync(admin);

        var booked = await ayse.PostAsJsonAsync("/api/reservations", new { resourceId = room, startDateTime = Start(10), endDateTime = Start(11) });
        booked.StatusCode.Should().Be(HttpStatusCode.Created);
        var ayses = (await booked.Content.ReadFromJsonAsync<Booking>(Json))!;

        // Free then: nothing to wait for.
        var free = await mehmet.PostAsJsonAsync("/api/waitlist", new { resourceId = room, startDateTime = Start(12), endDateTime = Start(13) });
        free.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var problem = JsonDocument.Parse(await free.Content.ReadAsStringAsync()))
        {
            problem.RootElement.GetProperty("code").GetString().Should().Be("waitlist-slot-free");
        }

        var joined = await mehmet.PostAsJsonAsync("/api/waitlist", new { resourceId = room, startDateTime = Start(10), endDateTime = Start(11) });
        joined.StatusCode.Should().Be(HttpStatusCode.Created);
        var entry = (await joined.Content.ReadFromJsonAsync<Entry>(Json))!;
        entry.Position.Should().Be(1);
        (await mehmet.GetFromJsonAsync<List<Entry>>("/api/waitlist", Json)).Should().ContainSingle(e => e.Id == entry.Id);

        (await ayse.PostAsync($"/api/reservations/{ayses.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = (await mehmet.GetFromJsonAsync<List<Booking>>("/api/reservations/mine", Json))!;
        mine.Should().ContainSingle(r => r.ResourceId == room && r.Status == "Confirmed" && r.StartDateTime == Start(10) && r.EndDateTime == Start(11));
        (await mehmet.GetFromJsonAsync<List<Entry>>("/api/waitlist", Json)).Should().BeEmpty();
        var inbox = (await mehmet.GetFromJsonAsync<List<Message>>("/api/inbox", Json))!;
        inbox.Should().Contain(m => m.Kind == "WaitlistBooked" && m.ReservationId == mine.Single(r => r.ResourceId == room).Id);
    }

    [Fact]
    public async Task Leaving_the_line_is_for_the_one_waiting()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var ayse = await ClientAsync("employee1@smartdesk.local");
        var mehmet = await ClientAsync("employee2@smartdesk.local");
        var room = await AddRoomAsync(admin);
        (await ayse.PostAsJsonAsync("/api/reservations", new { resourceId = room, startDateTime = Start(14), endDateTime = Start(15) }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var joined = await mehmet.PostAsJsonAsync("/api/waitlist", new { resourceId = room, startDateTime = Start(14), endDateTime = Start(15) });
        var entry = (await joined.Content.ReadFromJsonAsync<Entry>(Json))!;

        (await ayse.DeleteAsync($"/api/waitlist/{entry.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await mehmet.DeleteAsync($"/api/waitlist/{entry.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await mehmet.GetFromJsonAsync<List<Entry>>("/api/waitlist", Json)).Should().BeEmpty();
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static DateTime Start(int hour) => DateTime.UtcNow.Date.AddDays(95).AddHours(hour);

    private static async Task<Guid> AddRoomAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Waiting room {Guid.NewGuid():N}"[..21],
            resourceTypeId = ResourceTypeSeedData.MeetingRoomId,
            capacity = 6,
            location = "4th Floor",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Room>(Json))!.Id;
    }

    private async Task<HttpClient> ClientAsync(string email)
    {
        var response = await fixture.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email, password = IdentityDataSeeder.DemoPassword });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginBody>(Json))!;
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private sealed record LoginBody(string AccessToken);

    private sealed record Room(Guid Id);

    private sealed record Booking(Guid Id, Guid ResourceId, string Status, DateTime StartDateTime, DateTime EndDateTime);

    private sealed record Entry(Guid Id, Guid ResourceId, int Position);

    private sealed record Message(Guid Id, string Kind, Guid? ReservationId);
}
