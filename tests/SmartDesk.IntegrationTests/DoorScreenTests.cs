using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.IntegrationTests;

// A door screen over HTTP, on PostgreSQL (ADR-0034): set up by an admin, asked by a screen with
// no user, its changing code checking someone in while the printed one doesn't, and ending the
// booking at the door. Each test uses a room of its own.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DoorScreenTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_door_screens_changing_code_checks_in_and_the_printed_one_no_longer_does()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var ayse = await ClientAsync("employee1@smartdesk.local", "tr");
        var room = await AddRoomAsync(admin);
        var printed = (await admin.GetFromJsonAsync<PrintedCode>($"/api/resources/{room}/check-in-code"))!.Code;

        var setUp = await admin.PostAsync($"/api/resources/{room}/door-screen", null);
        setUp.StatusCode.Should().Be(HttpStatusCode.OK);
        var link = (await setUp.Content.ReadFromJsonAsync<Link>())!;
        link.Url.Should().Contain($"/door/{room}#");
        var token = link.Url[(link.Url.IndexOf('#') + 1)..];
        (await admin.GetFromJsonAsync<PrintedCode>($"/api/resources/{room}/check-in-code"))!.DoorScreenSince.Should().NotBeNull();

        // The screen has no user: just its token, in a header.
        var screen = fixture.CreateClient();
        (await screen.GetAsync($"/api/door/{room}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        screen.DefaultRequestHeaders.Add("X-Door-Token", token);

        var booked = await ayse.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room,
            startDateTime = DateTime.UtcNow.AddMinutes(-1),
            endDateTime = DateTime.UtcNow.AddMinutes(59),
        });
        booked.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await booked.Content.ReadFromJsonAsync<Booking>())!;

        var shown = (await screen.GetFromJsonAsync<Screen>($"/api/door/{room}?timeZone=Europe/Istanbul"))!;
        shown.Bookings.Should().ContainSingle(b => b.CheckInNeeded && !b.CheckedIn);
        var url = shown.CheckIn!.Url;
        var changing = url[(url.IndexOf("code=", StringComparison.Ordinal) + 5)..];
        (await screen.GetStringAsync($"/api/door/{room}")).Should().NotContain("Ayşe", "a screen in the corridor shows no names");

        // The printed code is refused, saying why, in the caller's language.
        var refused = await ayse.PostAsJsonAsync($"/api/resources/{room}/check-in", new { code = printed });
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using (var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            problem.RootElement.GetProperty("detail").GetString().Should().Contain("kapısındaki ekrandaki kodu yeniden okutun");
        }

        var checkedIn = await ayse.PostAsJsonAsync($"/api/resources/{room}/check-in", new { code = changing });
        checkedIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = (await checkedIn.Content.ReadFromJsonAsync<Booking>())!;
        dto.CheckedInAt.Should().NotBeNull();
        dto.CheckedInByName.Should().Be("Ayşe Yılmaz");
        (await screen.GetFromJsonAsync<Screen>($"/api/door/{room}"))!.Bookings.Single().CheckedIn.Should().BeTrue();

        // Done early: the room is free again at once.
        var ended = await ayse.PostAsync($"/api/reservations/{booking.Id}/end", null);
        ended.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ended.Content.ReadFromJsonAsync<Booking>())!.EndDateTime.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
        (await screen.GetFromJsonAsync<Screen>($"/api/door/{room}"))!.Bookings.Should().BeEmpty();

        // Turned off: the screen's link is dead, and the printed code is back.
        (await admin.DeleteAsync($"/api/resources/{room}/door-screen")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await screen.GetAsync($"/api/door/{room}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Only_those_who_may_edit_the_room_set_up_or_turn_off_its_screen()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var mehmet = await ClientAsync("employee2@smartdesk.local");
        var room = await AddRoomAsync(admin);

        (await mehmet.PostAsync($"/api/resources/{room}/door-screen", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await mehmet.DeleteAsync($"/api/resources/{room}/door-screen")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Checking_in_and_door_screens_have_limits_of_their_own()
    {
        await using var app = fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:CheckInPermitsPerMinute", "2");
            builder.UseSetting("RateLimiting:DoorScreenPermitsPerMinute", "2");
        });
        var admin = await ClientAsync(app, "admin@smartdesk.local");
        var ayse = await ClientAsync(app, "employee1@smartdesk.local");
        var mehmet = await ClientAsync(app, "employee2@smartdesk.local");
        var room = await AddRoomAsync(admin);

        // Guessing codes: refused as wrong, then not even tried; someone else still may.
        (await CheckInAsync(ayse, room, "0000")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        (await CheckInAsync(ayse, room, "1111")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
        var limited = await CheckInAsync(ayse, room, "2222");
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();
        (await DetailAsync(limited)).Should().Be("Too many check-in attempts. Wait a minute and try again.");
        (await CheckInAsync(mehmet, room, "3333")).StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests, "another user has a limit of their own");

        // A screen, or anyone else at that address, asking too often.
        var screen = app.CreateClient();
        (await screen.GetAsync($"/api/door/{room}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await screen.GetAsync($"/api/door/{room}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var flooded = await screen.GetAsync($"/api/door/{room}");
        flooded.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await DetailAsync(flooded)).Should().Be("The door screen asked too often. It tries again in a minute.");
    }

    private static Task<HttpResponseMessage> CheckInAsync(HttpClient client, Guid room, string code) =>
        client.PostAsJsonAsync($"/api/resources/{room}/check-in", new { code });

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("detail").GetString();
    }

    private static async Task<Guid> AddRoomAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Door room {Guid.NewGuid():N}"[..18],
            resourceTypeId = ResourceTypeSeedData.MeetingRoomId,
            capacity = 6,
            location = "5th Floor",
            checkInRequired = true,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Room>())!.Id;
    }

    private Task<HttpClient> ClientAsync(string email, string? acceptLanguage = null) =>
        ClientAsync(fixture, email, acceptLanguage);

    private static async Task<HttpClient> ClientAsync(
        WebApplicationFactory<Program> app, string email, string? acceptLanguage = null)
    {
        var response = await app.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email, password = IdentityDataSeeder.DemoPassword });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginBody>())!;
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        if (acceptLanguage is not null)
        {
            client.DefaultRequestHeaders.Add("Accept-Language", acceptLanguage);
        }

        return client;
    }

    private sealed record LoginBody(string AccessToken);

    private sealed record Room(Guid Id);

    private sealed record PrintedCode(string Code, string Url, DateTime? DoorScreenSince);

    private sealed record Link(string Url, DateTime SetUpAt);

    private sealed record Booking(Guid Id, DateTime EndDateTime, DateTime? CheckedInAt, string? CheckedInByName);

    private sealed record DoorBooking(DateTime StartDateTime, DateTime EndDateTime, bool CheckInNeeded, bool CheckedIn);

    private sealed record DoorCheckIn(string Url, DateTime ChangesAt);

    private sealed record Screen(string ResourceName, List<DoorBooking> Bookings, DoorCheckIn? CheckIn);
}
