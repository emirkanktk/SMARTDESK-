using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Domain.Entities;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;

namespace SmartDesk.IntegrationTests;

[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
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

        // Whether a loser was stopped by the handler's check or by the database constraint,
        // the client can tell it's an overlap (ADR-0023).
        foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
        {
            using var problem = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
            problem.RootElement.GetProperty("code").GetString().Should().Be("reservation-overlaps");
        }
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

    [Fact]
    public async Task A_clash_offers_the_nearest_free_times_and_other_rooms_free_then()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", await LoginAsync(client));

        var resources = await client.GetFromJsonAsync<List<NamedResource>>("/api/resources");
        var room = resources!.Single(r => r.Name == "Meeting Room A");
        var start = DateTime.UtcNow.Date.AddDays(93).AddHours(10);
        var first = await client.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room.Id,
            startDateTime = start,
            endDateTime = start.AddHours(1),
        });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        var clash = await client.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room.Id,
            startDateTime = start.AddMinutes(30),
            endDateTime = start.AddMinutes(90),
        });

        // ADR-0042: the hour before and the hour after, and the rooms free at that time.
        clash.StatusCode.Should().Be(HttpStatusCode.Conflict);
        using var problem = JsonDocument.Parse(await clash.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("code").GetString().Should().Be("reservation-overlaps");
        var suggestions = problem.RootElement.GetProperty("suggestions");
        suggestions.GetProperty("times").EnumerateArray()
            .Select(t => (t.GetProperty("start").GetDateTime(), t.GetProperty("end").GetDateTime()))
            .Should().Equal((start.AddHours(-1), start), (start.AddHours(1), start.AddHours(2)));
        var boardRoom = suggestions.GetProperty("resources").EnumerateArray()
            .Single(r => r.GetProperty("name").GetString() == "Board Room");
        boardRoom.GetProperty("capacity").GetInt32().Should().Be(16);
        boardRoom.GetProperty("location").GetString().Should().Be("5th Floor");
        boardRoom.GetProperty("id").GetGuid().Should().Be(resources!.Single(r => r.Name == "Board Room").Id);
    }

    [Fact]
    public async Task An_overlap_only_the_database_catches_is_still_a_coded_conflict()
    {
        // Straight to the database, past the handler's check and its lock, as two requests
        // racing past the check would be: the constraint's error becomes the same 409.
        using var scope = fixture.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartDeskDbContext>();
        var desk = await db.Resources.SingleAsync(r => r.Name == "Desk 3-02");
        var owner = await db.Users.SingleAsync(u => u.Email == "employee1@smartdesk.local");
        var start = DateTime.UtcNow.Date.AddDays(92).AddHours(10);
        db.Reservations.Add(new Reservation { Id = Guid.CreateVersion7(), ResourceId = desk.Id, UserId = owner.Id, StartDateTime = start, EndDateTime = start.AddHours(1) });
        await db.SaveChangesAsync();
        db.Reservations.Add(new Reservation { Id = Guid.CreateVersion7(), ResourceId = desk.Id, UserId = owner.Id, StartDateTime = start.AddMinutes(30), EndDateTime = start.AddHours(2) });

        var act = () => db.SaveChangesAsync();

        (await act.Should().ThrowAsync<ConflictException>()).Which.Code.Should().Be("reservation-overlaps");
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
