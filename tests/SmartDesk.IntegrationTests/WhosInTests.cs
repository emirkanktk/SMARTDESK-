using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.IntegrationTests;

// Who's in the office on a day (ADR-0063), through the real pipeline on PostgreSQL: a booking
// puts its owner in that day's list for whoever looks, their team marked.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class WhosInTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_booking_puts_its_owner_in_that_days_list_with_the_place_and_time()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var mehmet = await ClientAsync("employee2@smartdesk.local");
        var ayse = await ClientAsync("employee1@smartdesk.local");
        var room = await AddRoomAsync(admin);
        // Three days on, 12:00-13:00 in the office's Istanbul.
        var day = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var start = day.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
        var booked = await mehmet.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room.Id,
            startDateTime = start,
            endDateTime = start.AddHours(1),
        });
        booked.StatusCode.Should().Be(HttpStatusCode.Created);

        var whosIn = await ayse.GetFromJsonAsync<WhosInBody>($"/api/whos-in?date={day:yyyy-MM-dd}&timeZone=Europe/Istanbul");

        whosIn!.Date.Should().Be(day);
        whosIn.TimeZone.Should().Be("Europe/Istanbul");
        whosIn.HasTeam.Should().BeTrue("Ayşe is in Zeynep's team");
        var him = whosIn.People.Should().ContainSingle(p => p.Name == "Mehmet Demir").Subject;
        him.Should().Match<PersonBody>(p => p.InMyTeam && !p.IsMe);
        him.Visits.Should().ContainSingle(v => v.ResourceId == room.Id).Which.Should().Match<VisitBody>(v =>
            v.StartDateTime == start && v.ResourceTypeName == "Meeting Room" && v.Location == "1st Floor" && !v.Invited);
    }

    [Fact]
    public async Task A_day_or_zone_that_isnt_one_is_refused_and_a_guest_gets_nothing()
    {
        var ayse = await ClientAsync("employee1@smartdesk.local");

        (await ayse.GetAsync("/api/whos-in?timeZone=Mars/Olympus_Mons")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ayse.GetAsync("/api/whos-in?date=2026-13-40")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ayse.GetAsync("/api/whos-in?date=0001-01-01&timeZone=Europe/Istanbul")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ayse.GetAsync("/api/whos-in")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await fixture.CreateClient().GetAsync("/api/whos-in")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<ResourceRow> AddRoomAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Whos-in room {Guid.NewGuid():N}"[..22],
            resourceTypeId = ResourceTypeSeedData.MeetingRoomId,
            capacity = 4,
            location = "1st Floor",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ResourceRow>())!;
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

    private sealed record WhosInBody(DateOnly Date, string TimeZone, bool HasTeam, List<PersonBody> People);

    private sealed record PersonBody(Guid UserId, string Name, bool IsMe, bool InMyTeam, List<VisitBody> Visits);

    private sealed record VisitBody(
        Guid ReservationId,
        Guid ResourceId,
        string ResourceName,
        string ResourceTypeName,
        string Location,
        DateTime StartDateTime,
        DateTime EndDateTime,
        bool CheckedIn,
        bool Invited,
        string? OrganizerName);
}
