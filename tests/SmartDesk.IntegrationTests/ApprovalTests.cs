using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.IntegrationTests;

// Bookings that wait for an Admin's approval, through the real pipeline on PostgreSQL (ADR-0049):
// a company car asked for, held, approved or declined, and a request holding its time against
// a race and in the database itself. Each test uses a car of its own; the kind's setting is put
// back as the migration leaves it.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ApprovalTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_request_waits_for_an_admin_who_approves_or_declines_it()
    {
        var admin = await SignedInAsync("admin@smartdesk.local");
        var employee = await SignedInAsync("employee2@smartdesk.local");
        var car = await NewCarAsync(admin);
        await CarsNeedApprovalAsync(admin, true);
        try
        {
            var start = DateTime.UtcNow.Date.AddDays(150).AddHours(9);
            var asked = await employee.PostAsJsonAsync("/api/reservations", new { resourceId = car, startDateTime = start, endDateTime = start.AddHours(8) });
            asked.StatusCode.Should().Be(HttpStatusCode.Created);
            var request = (await asked.Content.ReadFromJsonAsync<Booking>())!;
            request.Status.Should().Be("Pending");
            request.RequestedAt.Should().NotBeNull();

            (await employee.GetAsync("/api/approvals")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
            (await admin.GetFromJsonAsync<List<Booking>>("/api/approvals"))!.Should().Contain(b => b.Id == request.Id);

            var approved = await admin.PostAsync($"/api/approvals/{request.Id}/approve", null);
            approved.StatusCode.Should().Be(HttpStatusCode.OK);
            (await approved.Content.ReadFromJsonAsync<Booking>())!.Status.Should().Be("Confirmed");
            var again = await admin.PostAsync($"/api/approvals/{request.Id}/approve", null);
            again.StatusCode.Should().Be(HttpStatusCode.Conflict);
            (await CodeAsync(again)).Should().Be("not-awaiting-approval");

            var next = start.AddDays(1);
            var second = (await (await employee.PostAsJsonAsync("/api/reservations", new { resourceId = car, startDateTime = next, endDateTime = next.AddHours(2) }))
                .Content.ReadFromJsonAsync<Booking>())!;
            (await admin.PostAsJsonAsync($"/api/approvals/{second.Id}/decline", new { reason = " " })).StatusCode
                .Should().Be(HttpStatusCode.BadRequest);
            var declined = await admin.PostAsJsonAsync($"/api/approvals/{second.Id}/decline", new { reason = "The car is booked for the board." });
            declined.StatusCode.Should().Be(HttpStatusCode.OK);
            var cancelled = (await declined.Content.ReadFromJsonAsync<Booking>())!;
            (cancelled.Status, cancelled.DeclineReason).Should().Be(("Cancelled", "The car is booked for the board."));
        }
        finally
        {
            await CarsNeedApprovalAsync(admin, false);
        }
    }

    [Fact]
    public async Task A_request_holds_its_time_against_a_race_and_in_the_database_itself()
    {
        var admin = await SignedInAsync("admin@smartdesk.local");
        var employee = await SignedInAsync("employee1@smartdesk.local");
        var car = await NewCarAsync(admin);
        await CarsNeedApprovalAsync(admin, true);
        try
        {
            var start = DateTime.UtcNow.Date.AddDays(151).AddHours(9);
            var body = new { resourceId = car, startDateTime = start, endDateTime = start.AddHours(1) };
            var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => employee.PostAsJsonAsync("/api/reservations", body)));

            responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
            foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
            {
                refused.StatusCode.Should().Be(HttpStatusCode.Conflict);
                (await CodeAsync(refused)).Should().Be("reservation-overlaps");
            }

            // Past every check, straight into the table: the constraint stops a second request too.
            using var scope = fixture.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SmartDeskDbContext>();
            db.Reservations.Add(new Reservation
            {
                Id = Guid.CreateVersion7(),
                ResourceId = car,
                UserId = Guid.NewGuid(),
                StartDateTime = start.AddMinutes(30),
                EndDateTime = start.AddMinutes(90),
                Status = ReservationStatus.Pending,
            });
            var insert = () => db.SaveChangesAsync();
            await insert.Should().ThrowAsync<ConflictException>();
        }
        finally
        {
            await CarsNeedApprovalAsync(admin, false);
        }
    }

    private static async Task CarsNeedApprovalAsync(HttpClient admin, bool needed) =>
        (await admin.PutAsJsonAsync($"/api/booking-rules/types/{ResourceTypeSeedData.CompanyCarId}", new
        {
            maxDurationMinutes = (int?)null,
            maxDaysAhead = (int?)null,
            maxActivePerUser = (int?)null,
            officeHoursOnly = false,
            requiresApproval = needed,
        })).EnsureSuccessStatusCode();

    // A car of its own, so no other test's bookings are in the way.
    private static async Task<Guid> NewCarAsync(HttpClient admin)
    {
        var created = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Approval car {Guid.NewGuid():N}"[..25],
            resourceTypeId = ResourceTypeSeedData.CompanyCarId,
            capacity = 5,
            location = "Parking",
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await created.Content.ReadFromJsonAsync<Created>())!.Id;
    }

    private static async Task<string?> CodeAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("code").GetString();
    }

    private async Task<HttpClient> SignedInAsync(string email)
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

    private sealed record Created(Guid Id);

    private sealed record Booking(Guid Id, string Status, DateTime? RequestedAt, string? DeclineReason);
}
