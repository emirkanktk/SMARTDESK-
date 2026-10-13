using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;

namespace SmartDesk.IntegrationTests;

// Issue #41 over HTTP, with PostgreSQL's locks: a resource with a booking still to come stays
// in service until the booking goes; bookings start at most 180 days ahead. And the rules an
// Admin sets for each kind of resource (ADR-0046), put back as they were after each test, since
// the database is shared.
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

    [Fact]
    public async Task An_admin_sets_the_rules_anyone_reads_them_and_bookings_keep_to_them()
    {
        var admin = await SignedInAsync("admin@smartdesk.local");
        var employee = await SignedInAsync("employee1@smartdesk.local");
        var deskType = await DeskTypeAsync(admin);
        var desk = await NewDeskAsync(admin, deskType);
        try
        {
            // Every day a workday, so whatever today is, a week from now the office is open.
            var hours = await admin.PutAsJsonAsync("/api/booking-rules/office-hours", new
            {
                timeZone = "UTC",
                opensAt = "08:30",
                closesAt = "17:00",
                workdays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" },
            });
            hours.StatusCode.Should().Be(HttpStatusCode.OK);
            using (var office = JsonDocument.Parse(await hours.Content.ReadAsStringAsync()))
            {
                office.RootElement.GetProperty("description").GetString().Should().Be("Mon-Sun 08:30-17:00");
            }

            var rules = new { maxDurationMinutes = 240, maxDaysAhead = 14, maxActivePerUser = (int?)null, officeHoursOnly = true };
            (await admin.PutAsJsonAsync($"/api/booking-rules/types/{deskType}", rules)).StatusCode.Should().Be(HttpStatusCode.OK);
            (await employee.PutAsJsonAsync($"/api/booking-rules/types/{deskType}", rules)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

            using (var read = JsonDocument.Parse(await employee.GetStringAsync("/api/booking-rules")))
            {
                read.RootElement.GetProperty("officeHours").GetProperty("opensAt").GetString().Should().Be("08:30:00");
                var desks = read.RootElement.GetProperty("types").EnumerateArray().Single(t => t.GetProperty("name").GetString() == "Desk");
                desks.GetProperty("maxDurationMinutes").GetInt32().Should().Be(240);
                desks.GetProperty("officeHoursOnly").GetBoolean().Should().BeTrue();
            }

            var day = DateTime.UtcNow.Date.AddDays(7);
            var late = await employee.PostAsJsonAsync(
                "/api/reservations", new { resourceId = desk, startDateTime = day.AddHours(18), endDateTime = day.AddHours(19) });
            late.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using (var problem = JsonDocument.Parse(await late.Content.ReadAsStringAsync()))
            {
                problem.RootElement.GetProperty("errors").GetProperty("StartDateTime")[0].GetString()
                    .Should().Be("Desk bookings must be within office hours: Mon-Sun 08:30-17:00.");
            }

            var inHours = await employee.PostAsJsonAsync(
                "/api/reservations", new { resourceId = desk, startDateTime = day.AddHours(9), endDateTime = day.AddHours(12) });
            inHours.StatusCode.Should().Be(HttpStatusCode.Created);
        }
        finally
        {
            await PutBackAsync(admin, deskType);
        }
    }

    [Fact]
    public async Task Bookings_made_at_once_dont_get_past_the_limit_per_person_together()
    {
        var admin = await SignedInAsync("admin@smartdesk.local");
        var employee = await SignedInAsync("employee2@smartdesk.local");
        var deskType = await DeskTypeAsync(admin);
        const int parallel = 6;
        var desks = new List<Guid>();
        for (var i = 0; i < parallel; i++)
        {
            desks.Add(await NewDeskAsync(admin, deskType));
        }

        try
        {
            // One more than they have already, whatever other tests booked for them.
            int upcoming;
            using (var scope = fixture.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<SmartDeskDbContext>();
                var mehmet = await db.Users.SingleAsync(u => u.Email == "employee2@smartdesk.local");
                upcoming = await db.Reservations.CountAsync(r => r.UserId == mehmet.Id
                    && r.Status == ReservationStatus.Confirmed
                    && r.EndDateTime > DateTime.UtcNow
                    && r.Resource.ResourceTypeId == deskType);
            }

            var rules = new { maxDurationMinutes = (int?)null, maxDaysAhead = (int?)null, maxActivePerUser = upcoming + 1, officeHoursOnly = false };
            (await admin.PutAsJsonAsync($"/api/booking-rules/types/{deskType}", rules)).StatusCode.Should().Be(HttpStatusCode.OK);

            // Each on a desk of its own, so only the limit can stop them, and each holds only its
            // own desk's lock: without one on the person, each would count what's committed and pass.
            var start = DateTime.UtcNow.Date.AddDays(120).AddHours(9);
            var responses = await Task.WhenAll(desks.Select(desk => employee.PostAsJsonAsync(
                "/api/reservations", new { resourceId = desk, startDateTime = start, endDateTime = start.AddHours(1) })));

            responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
            foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            {
                refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
                using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
                problem.RootElement.GetProperty("code").GetString().Should().Be("booking-limit");
            }
        }
        finally
        {
            await PutBackAsync(admin, deskType);
        }
    }

    private static async Task<Guid> DeskTypeAsync(HttpClient admin) =>
        (await admin.GetFromJsonAsync<List<NamedResource>>("/api/resource-types"))!.Single(t => t.Name == "Desk").Id;

    // A desk of its own, so no other test's bookings are in the way.
    private static async Task<Guid> NewDeskAsync(HttpClient admin, Guid deskType)
    {
        var created = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Rules desk {Guid.NewGuid():N}",
            resourceTypeId = deskType,
            capacity = 1,
            location = "Integration floor",
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<NamedResource>())!.Id;
    }

    // The rules as the migration leaves them.
    private static async Task PutBackAsync(HttpClient admin, Guid deskType)
    {
        (await admin.PutAsJsonAsync("/api/booking-rules/office-hours", new
        {
            timeZone = "Europe/Istanbul",
            opensAt = "09:00:00",
            closesAt = "18:00:00",
            workdays = new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" },
        })).EnsureSuccessStatusCode();
        (await admin.PutAsJsonAsync($"/api/booking-rules/types/{deskType}", new
        {
            maxDurationMinutes = (int?)null,
            maxDaysAhead = (int?)null,
            maxActivePerUser = (int?)null,
            officeHoursOnly = false,
        })).EnsureSuccessStatusCode();
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
