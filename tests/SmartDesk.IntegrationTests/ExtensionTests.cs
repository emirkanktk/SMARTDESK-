using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.IntegrationTests;

// Running over (ADR-0033) over HTTP, on PostgreSQL: how much longer a booking can run, and
// extending it. Each test books a room of its own.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ExtensionTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_booking_runs_longer_as_far_as_the_room_is_free()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var ayse = await ClientAsync("employee1@smartdesk.local", "tr");
        var mehmet = await ClientAsync("employee2@smartdesk.local");
        var room = await AddRoomAsync(admin);
        var mine = await BookAsync(ayse, room, Start(10), Start(11));

        (await ayse.GetFromJsonAsync<Options>($"/api/reservations/{mine.Id}/extension"))!.Minutes
            .Should().Equal(15, 30, 60);

        await BookAsync(mehmet, room, Start(11).AddMinutes(20), Start(12));
        (await ayse.GetFromJsonAsync<Options>($"/api/reservations/{mine.Id}/extension"))!.Minutes
            .Should().Equal(15);

        // Too long for the gap: refused, saying why, in the caller's language.
        var refused = await ayse.PostAsJsonAsync($"/api/reservations/{mine.Id}/extend", new { minutes = 30 });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            problem.RootElement.GetProperty("detail").GetString()
                .Should().EndWith("hemen sonrasında dolu; rezervasyon 30 dakika uzatılamıyor.");
        }

        var extended = await ayse.PostAsJsonAsync($"/api/reservations/{mine.Id}/extend", new { minutes = 15 });
        extended.StatusCode.Should().Be(HttpStatusCode.OK);
        (await extended.Content.ReadFromJsonAsync<Booking>())!.EndDateTime.Should().Be(Start(11).AddMinutes(15));

        // Only the offered lengths, and only the owner.
        (await ayse.PostAsJsonAsync($"/api/reservations/{mine.Id}/extend", new { minutes = 45 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await mehmet.PostAsJsonAsync($"/api/reservations/{mine.Id}/extend", new { minutes = 15 }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await mehmet.GetAsync($"/api/reservations/{mine.Id}/extension"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static DateTime Start(int hour) => DateTime.UtcNow.Date.AddDays(140).AddHours(hour);

    private static async Task<Booking> BookAsync(HttpClient client, Guid room, DateTime start, DateTime end)
    {
        var response = await client.PostAsJsonAsync("/api/reservations", new { resourceId = room, startDateTime = start, endDateTime = end });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Booking>())!;
    }

    private static async Task<Guid> AddRoomAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Extension room {Guid.NewGuid():N}"[..23],
            resourceTypeId = ResourceTypeSeedData.MeetingRoomId,
            capacity = 6,
            location = "4th Floor",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Room>())!.Id;
    }

    private async Task<HttpClient> ClientAsync(string email, string? acceptLanguage = null)
    {
        var response = await fixture.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email, password = IdentityDataSeeder.DemoPassword });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginBody>())!;
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        if (acceptLanguage is not null)
        {
            client.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);
        }

        return client;
    }

    private sealed record LoginBody(string AccessToken);

    private sealed record Room(Guid Id);

    private sealed record Booking(Guid Id, DateTime EndDateTime);

    private sealed record Options(List<int> Minutes);
}
