using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

[Collection(PostgresApiCollection.Name)]
public sealed class ReservationOverlapTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Simultaneous_bookings_of_one_slot_let_exactly_one_through()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client));

        var resources = await client.GetFromJsonAsync<List<NamedResource>>("/api/resources");
        var room = resources!.Single(r => r.Name == "Meeting Room A");
        var start = DateTime.UtcNow.Date.AddDays(90).AddHours(10);
        var body = new
        {
            resourceId = room.Id,
            startDateTime = start,
            endDateTime = start.AddHours(1),
            note = "integration race",
        };

        const int parallel = 8;
        var responses = await Task.WhenAll(
            Enumerable.Range(0, parallel).Select(_ => client.PostAsJsonAsync("/api/reservations", body)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(parallel - 1);
    }

    [Fact]
    public async Task Back_to_back_bookings_are_both_accepted()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client));

        var resources = await client.GetFromJsonAsync<List<NamedResource>>("/api/resources");
        var room = resources!.Single(r => r.Name == "Board Room");
        var start = DateTime.UtcNow.Date.AddDays(91).AddHours(10);

        var first = await client.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room.Id,
            startDateTime = start,
            endDateTime = start.AddMinutes(90),
        });
        var second = await client.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room.Id,
            startDateTime = start.AddMinutes(90),
            endDateTime = start.AddHours(3),
        });

        first.StatusCode.Should().Be(HttpStatusCode.Created);
        second.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private static async Task<string> LoginAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new
        {
            email = "employee1@smartdesk.local",
            password = IdentityDataSeeder.DemoPassword,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<LoginBody>();
        return body!.AccessToken;
    }

    private sealed record NamedResource(Guid Id, string Name);

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAt);
}
