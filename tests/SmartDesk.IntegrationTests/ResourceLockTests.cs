using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// The per-resource lock on PostgreSQL (ADR-0011). Two bookings of one slot also meet the
// exclusion constraint, but a booking and a block, or a booking and a change of status, meet
// nothing in the database: the lock each of them takes first is all that keeps both from going
// through. The unit tests run on SQLite, which has no row locks, so only here can it be seen
// to work.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ResourceLockTests(PostgresApiFixture fixture)
{
    // Each round races its requests for a new hour. Without the lock, two get through in some
    // rounds but not every one, so there are several.
    private const int Rounds = 10;

    [Fact]
    public async Task Blocks_and_bookings_of_one_slot_at_once_let_exactly_one_through()
    {
        var admin = await SignedInAsync("admin@smartdesk.local");
        var employee = await SignedInAsync("employee1@smartdesk.local");
        var desk = await AddDeskAsync(admin);

        for (var round = 0; round < Rounds; round++)
        {
            var start = DateTime.UtcNow.Date.AddDays(150).AddHours(8 + round);
            var block = new { resourceId = desk, startDateTime = start, endDateTime = start.AddHours(1), reason = "Integration race" };
            var booking = new { resourceId = desk, startDateTime = start, endDateTime = start.AddHours(1) };

            var responses = await RaceAsync(
                round,
                () => admin.PostAsJsonAsync("/api/resource-blocks", block),
                () => admin.PostAsJsonAsync("/api/resource-blocks", block),
                () => employee.PostAsJsonAsync("/api/reservations", booking),
                () => employee.PostAsJsonAsync("/api/reservations", booking),
                () => employee.PostAsJsonAsync("/api/reservations", booking));
            var blocks = responses[..2];
            var bookings = responses[2..];

            responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, $"one claim wins round {round}");
            if (blocks.Any(r => r.StatusCode == HttpStatusCode.Created))
            {
                (await CodesAsync(blocks.Where(r => r.StatusCode != HttpStatusCode.Created))).Should().Equal("block-overlaps-block");
                (await CodesAsync(bookings)).Should().OnlyContain(code => code == "resource-blocked");
            }
            else
            {
                (await CodesAsync(blocks)).Should().OnlyContain(code => code == "block-overlaps-reservations");
                (await CodesAsync(bookings.Where(r => r.StatusCode != HttpStatusCode.Created))).Should().OnlyContain(code => code == "reservation-overlaps");
            }
        }
    }

    [Fact]
    public async Task Taking_a_resource_out_of_service_and_booking_it_at_once_never_both_happen()
    {
        var admin = await SignedInAsync("admin@smartdesk.local");
        var employee = await SignedInAsync("employee1@smartdesk.local");
        var desk = await AddDeskAsync(admin);

        for (var round = 0; round < Rounds; round++)
        {
            var start = DateTime.UtcNow.Date.AddDays(151).AddHours(8 + round);
            var booking = new { resourceId = desk, startDateTime = start, endDateTime = start.AddHours(1) };

            var responses = await RaceAsync(
                round,
                () => admin.PatchAsJsonAsync($"/api/resources/{desk}/status", new { status = "Maintenance" }),
                () => employee.PostAsJsonAsync("/api/reservations", booking),
                () => employee.PostAsJsonAsync("/api/reservations", booking));
            var change = responses[0];
            var bookings = responses[1..];

            responses.Count(r => r.IsSuccessStatusCode).Should().Be(1, $"one of them wins round {round}");
            if (change.IsSuccessStatusCode)
            {
                (await CodesAsync(bookings)).Should().OnlyContain(code => code == "resource-unavailable");
                (await admin.PatchAsJsonAsync($"/api/resources/{desk}/status", new { status = "Active" })).EnsureSuccessStatusCode();
            }
            else
            {
                (await CodesAsync([change])).Should().Equal("resource-has-reservations");
                (await CodesAsync(bookings.Where(r => !r.IsSuccessStatusCode))).Should().Equal("reservation-overlaps");

                // Cancelled, so the next round starts with nothing booked.
                var booked = (await bookings.Single(r => r.IsSuccessStatusCode).Content.ReadFromJsonAsync<Identified>())!;
                (await employee.PostAsync($"/api/reservations/{booked.Id}/cancel", null)).EnsureSuccessStatusCode();
            }
        }
    }

    // Starts the requests together, first one first in round 0 and later ones first after
    // that, and gives back their responses in the order the requests were given.
    private static async Task<HttpResponseMessage[]> RaceAsync(
        int round, params Func<Task<HttpResponseMessage>>[] requests)
    {
        var responses = new Task<HttpResponseMessage>[requests.Length];
        for (var i = 0; i < requests.Length; i++)
        {
            var next = (i + round) % requests.Length;
            responses[next] = requests[next]();
        }

        return await Task.WhenAll(responses);
    }

    // Every response here must be a 409, and the code says which claim it lost to.
    private static async Task<List<string?>> CodesAsync(IEnumerable<HttpResponseMessage> responses)
    {
        var codes = new List<string?>();
        foreach (var response in responses)
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            codes.Add(problem.RootElement.GetProperty("code").GetString());
        }

        return codes;
    }

    // Its own desk, so no other test's bookings or blocks get in the way.
    private static async Task<Guid> AddDeskAsync(HttpClient admin)
    {
        var types = (await admin.GetFromJsonAsync<List<Named>>("/api/resource-types"))!;
        var created = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Lock desk {Guid.NewGuid():N}",
            resourceTypeId = types.Single(t => t.Name == "Desk").Id,
            capacity = 1,
            location = "Integration floor",
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<Identified>())!.Id;
    }

    private async Task<HttpClient> SignedInAsync(string email)
    {
        var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = IdentityDataSeeder.DemoPassword });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginBody>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.AccessToken);
        return client;
    }

    private sealed record Named(Guid Id, string Name);

    private sealed record Identified(Guid Id);

    private sealed record LoginBody(string AccessToken);
}
