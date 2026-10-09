using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.IntegrationTests;

// Checking in with a resource's QR code, through the real pipeline on PostgreSQL (ADR-0030).
// Each test books a room of its own, starting in a few minutes, so its check-in is open while
// the test runs and no other test's rooms are touched.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class CheckInTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_booking_is_checked_in_with_the_code_on_the_door()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var employee = await ClientAsync("employee2@smartdesk.local");
        var room = await AddRoomAsync(admin, checkIn: true);
        room.CheckInRequired.Should().BeTrue();

        var code = (await admin.GetFromJsonAsync<CodeBody>($"/api/resources/{room.Id}/check-in-code"))!;
        code.Code.Should().MatchRegex("^[0-9a-f]{32}$");
        code.Url.Should().Be($"http://localhost:4200/check-in/{room.Id}?code={code.Code}");
        (await employee.GetStringAsync($"/api/resources/{room.Id}")).Should().NotContain(code.Code, "only those who print it see it");

        var booked = await BookAsync(employee, room.Id);
        booked.CheckInRequired.Should().BeTrue();
        booked.CheckedInAt.Should().BeNull();

        var checkIn = await employee.PostAsJsonAsync($"/api/resources/{room.Id}/check-in", new { code = code.Code });

        checkIn.StatusCode.Should().Be(HttpStatusCode.OK);
        var checkedIn = (await checkIn.Content.ReadFromJsonAsync<ReservationBody>())!;
        checkedIn.Id.Should().Be(booked.Id);
        checkedIn.CheckedInAt.Should().NotBeNull();

        var again = await employee.PostAsJsonAsync($"/api/resources/{room.Id}/check-in", new { code = code.Code });
        again.StatusCode.Should().Be(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<ReservationBody>())!.CheckedInAt.Should().Be(checkedIn.CheckedInAt);
    }

    [Fact]
    public async Task A_check_in_that_cant_happen_says_why()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var employee = await ClientAsync("employee2@smartdesk.local");
        var inTurkish = await ClientAsync("employee1@smartdesk.local", "tr");
        var room = await AddRoomAsync(admin, checkIn: true);
        var printed = (await admin.GetFromJsonAsync<CodeBody>($"/api/resources/{room.Id}/check-in-code"))!.Code;
        await BookAsync(employee, room.Id);

        var replace = await admin.PostAsync($"/api/resources/{room.Id}/check-in-code", null);
        replace.StatusCode.Should().Be(HttpStatusCode.OK);
        var current = (await replace.Content.ReadFromJsonAsync<CodeBody>())!.Code;
        current.Should().NotBe(printed);

        // The old print.
        var old = await employee.PostAsJsonAsync($"/api/resources/{room.Id}/check-in", new { code = printed });
        old.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await DetailAsync(old)).Should().Be($"This QR code is no longer valid. Scan the current code for {room.Name}.");

        // Someone with no booking there, in Turkish.
        var none = await inTurkish.PostAsJsonAsync($"/api/resources/{room.Id}/check-in", new { code = current });
        none.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var problem = JsonDocument.Parse(await none.Content.ReadAsStringAsync()))
        {
            problem.RootElement.GetProperty("code").GetString().Should().Be("nothing-to-check-in");
            problem.RootElement.GetProperty("detail").GetString().Should().Be(
                $"Şu anda {room.Name} için check-in yapabileceğiniz bir rezervasyonunuz yok. Check-in, başlangıçtan 10 dakika önce açılır ve 15 dakika sonra kapanır.");
        }

        // A room that doesn't use check-in.
        var plain = await AddRoomAsync(admin, checkIn: false);
        var plainCode = (await admin.GetFromJsonAsync<CodeBody>($"/api/resources/{plain.Id}/check-in-code"))!.Code;
        var notUsed = await employee.PostAsJsonAsync($"/api/resources/{plain.Id}/check-in", new { code = plainCode });
        notUsed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var problem = JsonDocument.Parse(await notUsed.Content.ReadAsStringAsync()))
        {
            problem.RootElement.GetProperty("code").GetString().Should().Be("check-in-not-used");
        }

        // No code at all.
        (await employee.PostAsJsonAsync($"/api/resources/{room.Id}/check-in", new { code = "" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Anyone_can_read_the_check_in_times_and_the_grace_is_a_setting()
    {
        // Signed out, like a door screen: they say nothing about anyone (ADR-0040).
        var times = await fixture.CreateClient().GetFromJsonAsync<TimesBody>("/api/check-in/times");
        times.Should().Be(new TimesBody(10, 15, 5));

        await using var demo = fixture.WithWebHostBuilder(builder => builder.UseSetting("CheckIn:GraceMinutes", "1"));
        var oneMinute = await demo.CreateClient().GetFromJsonAsync<TimesBody>("/api/check-in/times");
        oneMinute.Should().Be(new TimesBody(10, 1, 1), "the warning can't come before the start");
    }

    [Fact]
    public void A_grace_out_of_range_stops_the_start()
    {
        using var app = fixture.WithWebHostBuilder(builder => builder.UseSetting("CheckIn:GraceMinutes", "0"));

        var act = () => app.CreateClient();

        act.Should().Throw<OptionsValidationException>().WithMessage("*CheckIn:GraceMinutes must be from 1 to 60*");
    }

    [Fact]
    public async Task Only_those_who_may_edit_a_resource_see_or_replace_its_code()
    {
        // The demo seed gives Meeting Room B to the demo manager's team; Board Room is shared.
        var employee = await ClientAsync("employee2@smartdesk.local");
        var manager = await ClientAsync("manager@smartdesk.local");
        var roomB = await FindAsync(manager, "Meeting Room B");
        var board = await FindAsync(manager, "Board Room");

        (await employee.GetAsync($"/api/resources/{roomB.Id}/check-in-code")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await employee.PostAsync($"/api/resources/{roomB.Id}/check-in-code", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.GetAsync($"/api/resources/{board.Id}/check-in-code")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.PostAsync($"/api/resources/{board.Id}/check-in-code", null)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await manager.GetAsync($"/api/resources/{roomB.Id}/check-in-code")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<ResourceRow> AddRoomAsync(HttpClient admin, bool checkIn)
    {
        var response = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Check-in room {Guid.NewGuid():N}"[..22],
            resourceTypeId = ResourceTypeSeedData.MeetingRoomId,
            capacity = 4,
            location = "1st Floor",
            checkInRequired = checkIn,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ResourceRow>())!;
    }

    // Starts in five minutes, so its check-in (from ten minutes before) is already open.
    private static async Task<ReservationBody> BookAsync(HttpClient client, Guid roomId)
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc).AddMinutes(5);
        var response = await client.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = roomId,
            startDateTime = start,
            endDateTime = start.AddMinutes(30),
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ReservationBody>())!;
    }

    private static async Task<string?> DetailAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("detail").GetString();
    }

    private static async Task<ResourceRow> FindAsync(HttpClient client, string name) =>
        (await client.GetFromJsonAsync<List<ResourceRow>>("/api/resources"))!.Single(r => r.Name == name);

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

    private sealed record ResourceRow(Guid Id, string Name, bool CheckInRequired);

    private sealed record ReservationBody(Guid Id, bool CheckInRequired, DateTime? CheckedInAt);

    private sealed record CodeBody(string Code, string Url);

    private sealed record TimesBody(int OpensBeforeMinutes, int GraceMinutes, int WarnBeforeReleaseMinutes);
}
