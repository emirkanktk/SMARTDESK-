using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using SmartDesk.API.Hubs;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// The reservation hub through the real pipeline on PostgreSQL, with SignalR's own client on a
// WebSocket and the token in the query string, where the browser puts it. The unit tests check
// who each event is addressed to; these check who actually hears it, and who can't listen at
// all (ADR-0011, ADR-0021, ADR-0022, ADR-0026).
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class RealtimeTests(PostgresApiFixture fixture)
{
    private const string CookieName = "smartdesk_session";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Nobody_listens_without_signing_in()
    {
        await using var guest = Connect(accessToken: null);

        (await RefusedAsync(guest)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_teams_resource_is_heard_by_its_team_and_the_admins_and_nobody_else()
    {
        // The demo seed gives Meeting Room B to the demo manager's team, employee1's; Meeting
        // Room A is shared. The outsider is an employee in no team.
        var member = await SignInAsync("employee1@smartdesk.local");
        var admin = await SignInAsync("admin@smartdesk.local");
        var outsider = await ChangePasswordAsync(await AddEmployeeAsync(admin, "hub.outsider@integration.example"));

        await using var memberHears = await ListenAsync(member);
        await using var adminHears = await ListenAsync(admin);
        await using var outsiderHears = await ListenAsync(outsider);

        var resources = (await Api(member).GetFromJsonAsync<List<ResourceRow>>("/api/resources"))!;
        var start = DateTime.UtcNow.Date.AddDays(140).AddHours(9);

        var teams = await BookAsync(member, resources.Single(r => r.Name == "Meeting Room B").Id, start);
        await memberHears.HearsAsync(ReservationHub.Created, teams);
        await adminHears.HearsAsync(ReservationHub.Created, teams);

        // One connection gets its events in the order they were sent, so by the time the
        // outsider hears this later booking, the team's would have arrived if it was sent.
        var shared = await BookAsync(member, resources.Single(r => r.Name == "Meeting Room A").Id, start);
        await outsiderHears.HearsAsync(ReservationHub.Created, shared);
        outsiderHears.Heard.Should().NotContain(e => e.ReservationId == teams);
    }

    [Fact]
    public async Task An_account_listens_only_once_its_password_is_its_own_and_not_after_it_is_disabled()
    {
        var admin = await SignInAsync("admin@smartdesk.local");
        var temporary = await AddEmployeeAsync(admin, "hub.leaver@integration.example");

        await using var beforeChange = Connect(temporary);
        (await RefusedAsync(beforeChange)).Should().Be(HttpStatusCode.Forbidden);

        var own = await ChangePasswordAsync(temporary);
        await using var hears = await ListenAsync(own);
        var id = (await Api(own).GetFromJsonAsync<Me>("/api/auth/me"))!.Id;

        (await Api(admin).PostAsync($"/api/users/{id}/disable", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await hears.Closed.WaitAsync(Patience);
        await using var again = Connect(own);
        (await RefusedAsync(again)).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_cuts_the_tabs_connection()
    {
        // A shared computer: once someone signs out, their open tab hears nothing more.
        var login = await fixture.CreateClient(new() { HandleCookies = false }).PostAsJsonAsync(
            "/api/auth/login", new { email = "employee2@smartdesk.local", password = IdentityDataSeeder.DemoPassword });
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<LoginBody>())!.AccessToken;
        await using var hears = await ListenAsync(token);

        var signOut = fixture.CreateClient(new() { HandleCookies = false });
        signOut.DefaultRequestHeaders.Add("Cookie", SessionCookie(login));
        (await signOut.PostAsync("/api/auth/logout", null)).EnsureSuccessStatusCode();

        await hears.Closed.WaitAsync(Patience);
        await using var again = Connect(token);
        (await RefusedAsync(again)).Should().Be(HttpStatusCode.Unauthorized);
    }

    private HubConnection Connect(string? accessToken)
    {
        var server = fixture.Server;
        var query = accessToken is null ? string.Empty : $"?access_token={Uri.EscapeDataString(accessToken)}";
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "/hubs/reservations" + query), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.WebSocketFactory = async (context, cancellationToken) =>
                    await server.CreateWebSocketClient().ConnectAsync(context.Uri, cancellationToken);
            })
            .Build();
    }

    private async Task<Listener> ListenAsync(string accessToken)
    {
        var listener = new Listener(Connect(accessToken));
        await listener.StartAsync();
        return listener;
    }

    // The negotiate request is where the token checks run, so a refusal is its status code.
    private static async Task<HttpStatusCode?> RefusedAsync(HubConnection connection)
    {
        var start = () => connection.StartAsync();
        return (await start.Should().ThrowAsync<HttpRequestException>()).Which.StatusCode;
    }

    private async Task<string> SignInAsync(string email)
    {
        var response = await fixture.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email, password = IdentityDataSeeder.DemoPassword });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginBody>())!.AccessToken;
    }

    // A new employee in no team, signed in with the temporary password the admin chose.
    private async Task<string> AddEmployeeAsync(string adminToken, string email)
    {
        (await Api(adminToken).PostAsJsonAsync("/api/users", new
        {
            firstName = "Hub",
            lastName = "Listener",
            email,
            role = "Employee",
            password = "Temp-Pass-2026",
        })).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await fixture.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email, password = "Temp-Pass-2026" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginBody>())!.AccessToken;
    }

    private async Task<string> ChangePasswordAsync(string temporaryToken)
    {
        var response = await Api(temporaryToken).PostAsJsonAsync(
            "/api/auth/change-password", new { currentPassword = "Temp-Pass-2026", newPassword = "Hub-Own-Pass-1" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<LoginBody>())!.AccessToken;
    }

    private async Task<Guid> BookAsync(string accessToken, Guid resourceId, DateTime start)
    {
        var response = await Api(accessToken).PostAsJsonAsync(
            "/api/reservations", new { resourceId, startDateTime = start, endDateTime = start.AddHours(1) });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<ReservationEvent>())!.Id;
    }

    private HttpClient Api(string accessToken)
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return client;
    }

    // The test server speaks plain http, where a client wouldn't send a Secure cookie back, so
    // it's sent by hand.
    private static string SessionCookie(HttpResponseMessage login) =>
        login.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(CookieName + "=", StringComparison.Ordinal)).Split(';')[0];

    // One hub connection and the reservation events it hears, in order.
    private sealed class Listener(HubConnection connection) : IAsyncDisposable
    {
        private readonly ConcurrentQueue<HeardEvent> _heard = new();
        private readonly SemaphoreSlim _arrived = new(0);
        private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IReadOnlyCollection<HeardEvent> Heard => [.. _heard];

        public Task Closed => _closed.Task;

        public async Task StartAsync()
        {
            foreach (var name in new[] { ReservationHub.Created, ReservationHub.Updated, ReservationHub.Cancelled })
            {
                connection.On<ReservationEvent>(name, reservation =>
                {
                    _heard.Enqueue(new HeardEvent(name, reservation.Id));
                    _arrived.Release();
                });
            }

            connection.Closed += _ =>
            {
                _closed.TrySetResult();
                return Task.CompletedTask;
            };

            await connection.StartAsync();
        }

        public async Task HearsAsync(string eventName, Guid reservationId)
        {
            using var timeout = new CancellationTokenSource(Patience);
            try
            {
                while (!_heard.Any(e => e.Name == eventName && e.ReservationId == reservationId))
                {
                    await _arrived.WaitAsync(timeout.Token);
                }
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"No {eventName} for {reservationId} within {Patience.TotalSeconds} s. Heard: {string.Join(", ", Heard)}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            await connection.DisposeAsync();
            _arrived.Dispose();
        }
    }

    private sealed record HeardEvent(string Name, Guid ReservationId);

    private sealed record ReservationEvent(Guid Id);

    private sealed record ResourceRow(Guid Id, string Name);

    private sealed record Me(Guid Id);

    private sealed record LoginBody(string AccessToken);
}
