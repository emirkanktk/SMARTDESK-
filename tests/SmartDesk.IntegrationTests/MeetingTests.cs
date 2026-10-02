using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence.Seed;

namespace SmartDesk.IntegrationTests;

// Inviting people to a booking and reading the inbox over HTTP, on PostgreSQL (ADR-0032). The
// demo seed puts both demo employees in the demo manager's team. Each test books a room of its
// own, so no other test's rooms are touched.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class MeetingTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task A_manager_invites_the_team_and_they_find_it_in_their_inbox()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var manager = await ClientAsync("manager@smartdesk.local");
        var ayse = await ClientAsync("employee1@smartdesk.local");
        var room = await AddRoomAsync(admin);

        var invitees = (await manager.GetFromJsonAsync<List<Invitee>>("/api/reservations/invitees"))!;
        invitees.Select(p => p.Name).Should().Contain(["Ayşe Yılmaz", "Mehmet Demir"]);
        (await ayse.GetFromJsonAsync<List<Invitee>>("/api/reservations/invitees")).Should().BeEmpty();
        var unreadBefore = (await ayse.GetFromJsonAsync<UnreadCount>("/api/inbox/unread-count"))!.Unread;

        var booked = await manager.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room,
            startDateTime = Start(10),
            endDateTime = Start(11),
            note = "Sprint planning",
            attendeeIds = invitees.Select(p => p.Id),
        });

        booked.StatusCode.Should().Be(HttpStatusCode.Created);
        var meeting = (await booked.Content.ReadFromJsonAsync<Meeting>())!;
        meeting.Attendees.Select(a => a.Name).Should().Equal(invitees.Select(p => p.Name).Order());

        var inbox = (await ayse.GetFromJsonAsync<List<Message>>("/api/inbox"))!;
        var invitation = inbox.First(m => m.ReservationId == meeting.Id);
        invitation.Kind.Should().Be("MeetingInvitation");
        invitation.SenderName.Should().Be("Zeynep Kaya");
        invitation.Note.Should().Be("Sprint planning");
        invitation.ReadAt.Should().BeNull();
        (await ayse.GetFromJsonAsync<UnreadCount>("/api/inbox/unread-count"))!.Unread.Should().Be(unreadBefore + 1);
        (await ayse.GetFromJsonAsync<List<Meeting>>("/api/reservations/invited"))!.Should().Contain(r => r.Id == meeting.Id);

        (await ayse.PostAsync($"/api/inbox/{invitation.Id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ayse.GetFromJsonAsync<UnreadCount>("/api/inbox/unread-count"))!.Unread.Should().Be(unreadBefore);
        (await manager.PostAsync($"/api/inbox/{invitation.Id}/read", null)).StatusCode.Should().Be(HttpStatusCode.NotFound, "it isn't hers");

        // Cancelling reaches the inbox too.
        (await manager.PostAsync($"/api/reservations/{meeting.Id}/cancel", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ayse.GetFromJsonAsync<List<Message>>("/api/inbox"))!
            .Should().Contain(m => m.ReservationId == meeting.Id && m.Kind == "MeetingCancelled");
        (await ayse.PostAsync("/api/inbox/read-all", null)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ayse.GetFromJsonAsync<UnreadCount>("/api/inbox/unread-count"))!.Unread.Should().Be(0);
    }

    [Fact]
    public async Task An_invitation_that_cant_happen_says_why_next_to_the_list()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var ayse = await ClientAsync("employee1@smartdesk.local", "tr");
        var mehmetId = (await (await ClientAsync("employee2@smartdesk.local")).GetFromJsonAsync<Me>("/api/auth/me"))!.Id;
        var room = await AddRoomAsync(admin);

        var refused = await ayse.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room,
            startDateTime = Start(14),
            endDateTime = Start(15),
            attendeeIds = new[] { mehmetId },
        });

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errors").GetProperty("AttendeeIds")[0].GetString()
            .Should().Be("Yalnızca ekip yöneticileri ve sistem yöneticileri kişi davet edebilir.");
        (await ayse.GetFromJsonAsync<List<Meeting>>("/api/reservations/mine"))!.Should().NotContain(r => r.ResourceId == room);
    }

    [Fact]
    public async Task Someone_invited_answers_and_the_organizer_hears_why_they_cant_come()
    {
        var admin = await ClientAsync("admin@smartdesk.local");
        var manager = await ClientAsync("manager@smartdesk.local");
        var mehmet = await ClientAsync("employee2@smartdesk.local", "tr");
        var mehmetId = (await mehmet.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;
        var room = await AddRoomAsync(admin);
        var booked = await manager.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room,
            startDateTime = Start(16),
            endDateTime = Start(17),
            attendeeIds = new[] { mehmetId },
        });
        var meeting = (await booked.Content.ReadFromJsonAsync<Meeting>())!;
        meeting.Attendees.Single().Response.Should().Be("Pending");

        // Not coming says why: without a reason it's refused, in Turkish, next to the field.
        var refused = await mehmet.PostAsJsonAsync($"/api/reservations/{meeting.Id}/response", new { response = "Declined" });
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using (var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync()))
        {
            problem.RootElement.GetProperty("errors").GetProperty("Reason")[0].GetString()
                .Should().Be("Neden katılamayacağınızı yazın.");
        }

        var declined = await mehmet.PostAsJsonAsync(
            $"/api/reservations/{meeting.Id}/response", new { response = "Declined", reason = "Müşteri ziyaretindeyim" });
        declined.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await declined.Content.ReadAsStringAsync();
        JsonSerializer.Deserialize<Meeting>(body, Json)!.Attendees.Single().Response.Should().Be("Declined");
        body.Should().NotContain("Müşteri", "the reason is for the organizer only, not everyone who sees the booking");

        var message = (await manager.GetFromJsonAsync<List<Message>>("/api/inbox"))!.First(m => m.ReservationId == meeting.Id);
        message.Kind.Should().Be("MeetingDeclined");
        message.Reason.Should().Be("Müşteri ziyaretindeyim");
        message.SenderName.Should().Be("Mehmet Demir");

        // Someone who isn't on the list can't answer for it.
        var ayse = await ClientAsync("employee1@smartdesk.local");
        (await ayse.PostAsJsonAsync($"/api/reservations/{meeting.Id}/response", new { response = "Accepted" }))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static DateTime Start(int hour) => DateTime.UtcNow.Date.AddDays(130).AddHours(hour);

    private static async Task<Guid> AddRoomAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Meeting room {Guid.NewGuid():N}"[..21],
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

    private sealed record Invitee(Guid Id, string Name, string Email);

    private sealed record Attendee(Guid Id, string? Name, string Response);

    private sealed record Meeting(Guid Id, Guid ResourceId, List<Attendee> Attendees);

    private sealed record Message(
        Guid Id, string Kind, Guid? ReservationId, string? SenderName, string? Note, string? Reason, DateTime? ReadAt);

    private sealed record UnreadCount(int Unread);
}
