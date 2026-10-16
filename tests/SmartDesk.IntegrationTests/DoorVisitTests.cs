using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.IntegrationTests;

// The page a QR code opens for someone without an account, over HTTP, on PostgreSQL (ADR-0050):
// the code alone opens it, checks in whoever's booking is waiting, and reports a problem, with
// nobody's name in anything it gets back. Each test uses a room of its own.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class DoorVisitTests(PostgresApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task Without_an_account_the_code_opens_the_page_checks_in_and_reports_a_problem()
    {
        var admin = await ClientAsync(fixture, "admin@smartdesk.local");
        var ayse = await ClientAsync(fixture, "employee1@smartdesk.local");
        var room = await AddRoomAsync(admin);
        var code = (await admin.GetFromJsonAsync<PrintedCode>($"/api/resources/{room}/check-in-code"))!.Code;
        var booked = await ayse.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room,
            startDateTime = DateTime.UtcNow.AddMinutes(-1),
            endDateTime = DateTime.UtcNow.AddMinutes(59),
            note = "Budget talk",
        });
        booked.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await booked.Content.ReadFromJsonAsync<Booking>(Json))!;

        // Nobody signed in: only the code, in a header.
        var phone = fixture.CreateClient();
        (await phone.GetAsync($"/api/door/{room}/visit")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        phone.DefaultRequestHeaders.Add("X-Check-In-Code", code);
        var page = await phone.GetAsync($"/api/door/{room}/visit?timeZone=Europe/Istanbul");
        page.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await page.Content.ReadAsStringAsync();
        body.Should().NotContain("Ayşe").And.NotContain("Budget", "a corridor is no place for names");
        var visit = JsonSerializer.Deserialize<Visit>(body, Json)!;
        visit.ResourceName.Should().StartWith("Visit room");
        visit.CheckInRequired.Should().BeTrue();
        visit.Bookings.Should().ContainSingle(b => b.CheckInNeeded && !b.CheckedIn);

        // The signed-in way still needs an account; the door's own takes the code.
        var anonymous = fixture.CreateClient();
        (await anonymous.PostAsJsonAsync($"/api/resources/{room}/check-in", new { code }))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var checkedIn = await anonymous.PostAsJsonAsync($"/api/door/{room}/check-in", new { code });
        checkedIn.StatusCode.Should().Be(HttpStatusCode.OK);
        (await checkedIn.Content.ReadAsStringAsync()).Should().NotContain("Ayşe");
        (await phone.GetFromJsonAsync<Visit>($"/api/door/{room}/visit", Json))!.Bookings.Single().CheckedIn.Should().BeTrue();
        var mine = (await ayse.GetFromJsonAsync<List<Booking>>("/api/reservations/mine", Json))!.Single(b => b.Id == booking.Id);
        mine.CheckedInAt.Should().NotBeNull();
        mine.CheckedInByName.Should().BeNull("nobody signed in did it");

        // A problem, reported without an account: those who look after the room see it, from nobody.
        var form = new MultipartFormDataContent
        {
            { new StringContent(code), "code" },
            { new StringContent("Display"), "category" },
            { new StringContent("The screen stays black"), "description" },
            { new StringContent("false"), "isBlocking" },
        };
        var reported = await anonymous.PostAsync($"/api/door/{room}/issues", form);
        reported.StatusCode.Should().Be(HttpStatusCode.Created);
        var issue = (await reported.Content.ReadFromJsonAsync<IssueRow>(Json))!;
        issue.ReportedByUserId.Should().BeNull();
        var listed = (await admin.GetFromJsonAsync<List<IssueRow>>($"/api/issues?resourceId={room}", Json))!;
        listed.Should().ContainSingle(i => i.Id == issue.Id && i.ReportedByName == null);
        (await phone.GetFromJsonAsync<Visit>($"/api/door/{room}/visit", Json))!.OpenIssues
            .Should().ContainSingle(i => i.Description == "The screen stays black");

        // A wrong code gets nothing, not even whether the room is there.
        var wrong = new MultipartFormDataContent
        {
            { new StringContent(new string('0', 32)), "code" },
            { new StringContent("Cleaning"), "category" },
            { new StringContent("Crumbs everywhere"), "description" },
        };
        (await anonymous.PostAsync($"/api/door/{room}/issues", wrong)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await anonymous.PostAsJsonAsync($"/api/door/{room}/check-in", new { code = new string('0', 32) }))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Without_an_account_the_doors_limits_go_by_address()
    {
        await using var app = fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:CheckInPermitsPerMinute", "2");
            builder.UseSetting("RateLimiting:DoorPagePermitsPerMinute", "2");
        });
        var admin = await ClientAsync(app, "admin@smartdesk.local");
        var ayse = await ClientAsync(app, "employee1@smartdesk.local");
        var room = await AddRoomAsync(admin);

        // Guessing codes for the page, then for a check-in, from one address.
        var phone = app.CreateClient();
        (await phone.GetAsync($"/api/door/{room}/visit")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await phone.GetAsync($"/api/door/{room}/visit")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var flooded = await phone.GetAsync($"/api/door/{room}/visit");
        flooded.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await DetailAsync(flooded)).Should().Be("Too many requests for this page in a short time. Wait a minute and try again.");

        (await phone.PostAsJsonAsync($"/api/door/{room}/check-in", new { code = "0000" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await phone.PostAsJsonAsync($"/api/door/{room}/check-in", new { code = "1111" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var limited = await phone.PostAsJsonAsync($"/api/door/{room}/check-in", new { code = "2222" });
        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await DetailAsync(limited)).Should().Be("Too many check-in attempts. Wait a minute and try again.");

        // Someone signed in still has a limit of their own.
        (await ayse.PostAsJsonAsync($"/api/resources/{room}/check-in", new { code = "3333" }))
            .StatusCode.Should().NotBe(HttpStatusCode.TooManyRequests);
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("detail").GetString();
    }

    private static async Task<Guid> AddRoomAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Visit room {Guid.NewGuid():N}"[..19],
            resourceTypeId = ResourceTypeSeedData.MeetingRoomId,
            capacity = 6,
            location = "5th Floor",
            checkInRequired = true,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Room>())!.Id;
    }

    private static async Task<HttpClient> ClientAsync(WebApplicationFactory<Program> app, string email)
    {
        var response = await app.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email, password = IdentityDataSeeder.DemoPassword });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginBody>())!;
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private sealed record LoginBody(string AccessToken);

    private sealed record Room(Guid Id);

    private sealed record PrintedCode(string Code);

    private sealed record Booking(Guid Id, DateTime? CheckedInAt, string? CheckedInByName);

    private sealed record DoorBooking(DateTime StartDateTime, DateTime EndDateTime, bool CheckInNeeded, bool CheckedIn);

    private sealed record DoorIssue(Guid Id, string Category, string Description, bool IsBlocking, string Status);

    private sealed record Visit(
        string ResourceName, bool CheckInRequired, List<DoorBooking> Bookings, List<DoorIssue> OpenIssues);

    private sealed record IssueRow(Guid Id, Guid? ReportedByUserId, string? ReportedByName);
}
