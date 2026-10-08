using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// Closing a resource while a problem is fixed, through the real pipeline on PostgreSQL
// (ADR-0038): its bookings in the window go with the block only when asked, and their people
// hear why.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class BlockCancelsBookingsTests(PostgresApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    [Fact]
    public async Task A_block_takes_the_bookings_in_its_window_only_when_asked_and_their_owners_hear_why()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var owner = await ClientAsync("employee1@smartdesk.local");
        var desk = await AddDeskAsync(admin);
        var start = DateTime.UtcNow.Date.AddDays(150).AddHours(9);

        var booked = await owner.PostAsJsonAsync(
            "/api/reservations", new { resourceId = desk, startDateTime = start.AddHours(1), endDateTime = start.AddHours(2) });
        booked.StatusCode.Should().Be(HttpStatusCode.Created);
        var booking = (await booked.Content.ReadFromJsonAsync<Row>(Json))!;

        var block = new { resourceId = desk, startDateTime = start, endDateTime = start.AddHours(8), reason = "Air conditioning broken" };
        var refused = await admin.PostAsJsonAsync("/api/resource-blocks", block);
        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await refused.Content.ReadFromJsonAsync<Problem>(Json))!.Code.Should().Be("block-overlaps-reservations");

        var created = await admin.PostAsJsonAsync("/api/resource-blocks", new
        {
            block.resourceId,
            block.startDateTime,
            block.endDateTime,
            block.reason,
            cancelReservations = true,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);

        (await owner.GetFromJsonAsync<Row>($"/api/reservations/{booking.Id}", Json))!.Status.Should().Be("Cancelled");
        var inbox = (await owner.GetFromJsonAsync<List<InboxRow>>("/api/inbox", Json))!;
        inbox.Should().ContainSingle(m => m.Kind == "CancelledByBlock" && m.ReservationId == booking.Id
            && m.Reason == "Air conditioning broken" && m.SenderName != null);

        // Closed: nobody books into it until it's lifted.
        var again = await owner.PostAsJsonAsync(
            "/api/reservations", new { resourceId = desk, startDateTime = start.AddHours(1), endDateTime = start.AddHours(2) });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await again.Content.ReadFromJsonAsync<Problem>(Json))!.Code.Should().Be("resource-blocked");
    }

    // A shared desk of its own, so no other booking is in the way.
    private static async Task<Guid> AddDeskAsync(HttpClient admin)
    {
        var types = (await admin.GetFromJsonAsync<List<NamedRow>>("/api/resource-types"))!;
        var created = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Block desk {Guid.NewGuid():N}",
            resourceTypeId = types.Single(t => t.Name == "Desk").Id,
            capacity = 1,
            location = "Integration floor",
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<NamedRow>())!.Id;
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

    private sealed record NamedRow(Guid Id, string Name);

    private sealed record Row(Guid Id, string Status);

    private sealed record InboxRow(string Kind, Guid? ReservationId, string? Reason, string? SenderName);

    private sealed record Problem(string? Code);
}
