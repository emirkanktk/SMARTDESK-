using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// Issue #41 over HTTP, with PostgreSQL's locks: a resource with a booking still to come stays
// in service until the booking goes; bookings start at most 180 days ahead.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class BookingRulesTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_resource_leaves_service_only_once_its_bookings_are_gone()
    {
        var employee = await SignedInAsync("employee1@smartdesk.local");
        var admin = await SignedInAsync("admin@smartdesk.local");
        // Its own resource, so no other test's bookings count.
        var types = (await admin.GetFromJsonAsync<List<NamedResource>>("/api/resource-types"))!;
        var created = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Rules desk {Guid.NewGuid():N}",
            resourceTypeId = types.Single(t => t.Name == "Desk").Id,
            capacity = 1,
            location = "Integration floor",
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var desk = (await created.Content.ReadFromJsonAsync<NamedResource>())!;
        var start = DateTime.UtcNow.Date.AddDays(100).AddHours(9);
        var booked = await employee.PostAsJsonAsync("/api/reservations", new { resourceId = desk.Id, startDateTime = start, endDateTime = start.AddHours(2) });
        booked.StatusCode.Should().Be(HttpStatusCode.Created);
        var reservation = (await booked.Content.ReadFromJsonAsync<Identified>())!;

        var refused = await admin.PatchAsJsonAsync($"/api/resources/{desk.Id}/status", new { status = "Maintenance" });

        refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using (var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            problem.RootElement.GetProperty("code").GetString().Should().Be("resource-has-reservations");
        }

        (await employee.PostAsync($"/api/reservations/{reservation.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PatchAsJsonAsync($"/api/resources/{desk.Id}/status", new { status = "Maintenance" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PatchAsJsonAsync($"/api/resources/{desk.Id}/status", new { status = "Active" })).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Nobody_books_more_than_180_days_ahead()
    {
        var employee = await SignedInAsync("employee1@smartdesk.local");
        var desk = (await employee.GetFromJsonAsync<List<NamedResource>>("/api/resources"))!.Single(r => r.Name == "Desk 3-02");
        var start = DateTime.UtcNow.AddDays(181);

        var response = await employee.PostAsJsonAsync("/api/reservations", new { resourceId = desk.Id, startDateTime = start, endDateTime = start.AddHours(1) });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errors").GetProperty("StartDateTime")[0].GetString()
            .Should().Be("A reservation can start at most 180 days ahead.");
    }

    private async Task<HttpClient> SignedInAsync(string email)
    {
        var client = fixture.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = IdentityDataSeeder.DemoPassword });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginBody>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private sealed record NamedResource(Guid Id, string Name);

    private sealed record Identified(Guid Id);

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAt);
}
