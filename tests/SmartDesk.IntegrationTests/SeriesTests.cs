using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.IntegrationTests;

// A booking that repeats over HTTP, on PostgreSQL (ADR-0035): seen first, refused while a date
// is taken, booked around it when asked, answered once for every date, and ended early. Each
// test books a room of its own.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SeriesTests(PostgresApiFixture fixture)
{
    private const string Zone = "Europe/Istanbul";

    [Fact]
    public async Task A_weekly_meeting_is_previewed_booked_around_a_taken_date_answered_once_and_ended_early()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var manager = await ClientAsync("manager@smartdesk.local");
        var ayse = await ClientAsync("employee1@smartdesk.local");
        var ayseId = (await ayse.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;
        var room = await AddRoomAsync(admin);

        // 10:00-11:00 in Istanbul, the same weekday for four weeks; someone else has the third.
        var first = DateTime.UtcNow.Date.AddDays(100).AddHours(7);
        var day = first.DayOfWeek.ToString();
        var until = DateOnly.FromDateTime(first.AddDays(21));
        (await ayse.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room,
            startDateTime = first.AddDays(14),
            endDateTime = first.AddDays(14).AddHours(1),
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        // What the form shows before booking.
        var preview = (await manager.GetFromJsonAsync<Preview>(
            $"/api/reservations/series/preview?resourceId={room}&startDateTime={Iso(first)}&endDateTime={Iso(first.AddHours(1))}" +
            $"&frequency=Weekly&interval=1&daysOfWeek={day}&until={until:yyyy-MM-dd}&timeZone={Uri.EscapeDataString(Zone)}"))!;
        preview.Dates.Select(d => d.StartDateTime).Should().Equal(first, first.AddDays(7), first.AddDays(14), first.AddDays(21));
        preview.Dates.Select(d => d.Free).Should().Equal(true, true, false, true);

        var recurrence = new { frequency = "Weekly", interval = 1, daysOfWeek = new[] { day }, until = $"{until:yyyy-MM-dd}", timeZone = Zone };
        var refused = await manager.PostAsJsonAsync("/api/reservations/series", new
        {
            resourceId = room,
            startDateTime = first,
            endDateTime = first.AddHours(1),
            note = "Weekly sync",
            attendeeIds = new[] { ayseId },
            recurrence,
        });
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            problem.RootElement.GetProperty("code").GetString().Should().Be("series-conflicts");
        }

        var booked = await manager.PostAsJsonAsync("/api/reservations/series", new
        {
            resourceId = room,
            startDateTime = first,
            endDateTime = first.AddHours(1),
            note = "Weekly sync",
            attendeeIds = new[] { ayseId },
            recurrence,
            skipConflicts = true,
        });
        booked.StatusCode.Should().Be(HttpStatusCode.Created);
        var result = (await booked.Content.ReadFromJsonAsync<SeriesResult>())!;
        result.Booked.Select(r => r.StartDateTime).Should().Equal(first, first.AddDays(7), first.AddDays(21));
        result.Skipped.Should().ContainSingle(d => d.StartDateTime == first.AddDays(14) && d.Code == "reservation-overlaps");
        result.Series.DaysOfWeek.Should().Equal(day);
        result.Series.Until.Should().Be(until);
        result.Booked.Should().OnlyContain(r => r.Series!.Id == result.Series.Id);

        // One invitation for the series, which Ayşe answers for every date at once.
        var invitation = (await ayse.GetFromJsonAsync<List<Message>>("/api/inbox"))!
            .Single(m => m.Series?.Id == result.Series.Id);
        invitation.Kind.Should().Be("SeriesInvitation");
        invitation.ReservationId.Should().Be(result.Booked[0].Id);
        (await ayse.PostAsJsonAsync($"/api/reservations/{invitation.ReservationId}/response", new { response = "Accepted", following = true }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await ayse.GetFromJsonAsync<List<Booking>>("/api/reservations/invited"))!
            .Where(r => r.Series?.Id == result.Series.Id)
            .Should().HaveCount(3).And.OnlyContain(r => r.Attendees.Single().Response == "Accepted");
        (await manager.GetFromJsonAsync<List<Message>>("/api/inbox"))!
            .Should().ContainSingle(m => m.Series != null && m.Series.Id == result.Series.Id && m.Kind == "SeriesAccepted");

        // Ended from its second date: the first stays.
        var cancelled = await manager.PostAsync($"/api/reservations/{result.Booked[1].Id}/cancel-following", null);
        cancelled.StatusCode.Should().Be(HttpStatusCode.OK);
        (await cancelled.Content.ReadFromJsonAsync<List<Booking>>())!
            .Select(r => r.Id).Should().Equal(result.Booked[1].Id, result.Booked[2].Id);
        var mine = (await manager.GetFromJsonAsync<List<Booking>>("/api/reservations/mine"))!
            .Where(r => r.Series?.Id == result.Series.Id)
            .ToDictionary(r => r.Id, r => r.Status);
        mine.Should().Equal(new Dictionary<Guid, string>
        {
            [result.Booked[0].Id] = "Confirmed",
            [result.Booked[1].Id] = "Cancelled",
            [result.Booked[2].Id] = "Cancelled",
        });
        (await ayse.GetFromJsonAsync<List<Message>>("/api/inbox"))!
            .Should().ContainSingle(m => m.Series != null && m.Series.Id == result.Series.Id && m.Kind == "SeriesCancelled");
    }

    [Fact]
    public async Task A_rule_that_cant_be_says_which_field_is_wrong_in_the_callers_language()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var ayse = await ClientAsync("employee1@smartdesk.local", "tr");
        var room = await AddRoomAsync(admin);
        var first = DateTime.UtcNow.Date.AddDays(100).AddHours(7);

        var refused = await ayse.PostAsJsonAsync("/api/reservations/series", new
        {
            resourceId = room,
            startDateTime = first,
            endDateTime = first.AddHours(1),
            recurrence = new { frequency = "Weekly", interval = 1, until = $"{DateOnly.FromDateTime(first):yyyy-MM-dd}", timeZone = Zone },
        });

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errors").GetProperty("Recurrence.Until")[0].GetString()
            .Should().Be("Bu yalnızca bir tarih: tekrarlaması için daha geç bir bitiş ya da daha fazla gün seçin.");
    }

    private static string Iso(DateTime utc) => Uri.EscapeDataString(utc.ToString("yyyy-MM-ddTHH:mm:ssZ"));

    private static async Task<Guid> AddRoomAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Series room {Guid.NewGuid():N}"[..20],
            resourceTypeId = ResourceTypeSeedData.MeetingRoomId,
            capacity = 6,
            location = "3rd Floor",
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

    private sealed record Me(Guid Id);

    private sealed record Room(Guid Id);

    private sealed record PreviewDate(DateTime StartDateTime, DateTime EndDateTime, bool Free, string? Code, string? Reason);

    private sealed record Preview(List<PreviewDate> Dates);

    private sealed record Rule(Guid Id, string Frequency, int Interval, List<string> DaysOfWeek, DateOnly Until, string TimeZone);

    private sealed record Attendee(Guid Id, string? Name, string Response);

    private sealed record Booking(Guid Id, DateTime StartDateTime, string Status, Rule? Series, List<Attendee> Attendees);

    private sealed record SeriesResult(Rule Series, List<Booking> Booked, List<PreviewDate> Skipped);

    private sealed record Message(Guid Id, string Kind, Guid? ReservationId, Rule? Series);
}
