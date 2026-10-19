using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Core;
using Serilog.Events;
using SmartDesk.API.Logging;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// Structured logs (ADR-0053): a line for every request, saying whose it was, and nothing a query
// string carries - SignalR's token, a calendar feed's - in any line.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class LoggingTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Every_request_is_a_line_with_its_status_time_and_whose_it_was()
    {
        var log = new LogLines();
        await using var app = AppWritingTo(log);
        var (ayse, _) = await SignedInAsync(app, "employee1@smartdesk.local");
        var me = (await ayse.GetFromJsonAsync<Me>("/api/auth/me"))!;

        (await ayse.GetAsync("/api/reservations/mine")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.CreateClient().GetAsync("/api/reservations/mine")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var hers = await log.RequestAsync("GET", "/api/reservations/mine", 200);
        hers.Level.Should().Be(LogEventLevel.Information);
        hers.Properties[LoggingSetup.UserIdProperty].Should().Be(new ScalarValue(me.Id));
        hers.Properties.Should().ContainKey("Elapsed");
        (await log.RequestAsync("GET", "/api/reservations/mine", 401))
            .Properties.Should().NotContainKey(LoggingSetup.UserIdProperty);
    }

    [Fact]
    public async Task What_a_query_string_carries_never_reaches_the_log()
    {
        var log = new LogLines();
        await using var app = AppWritingTo(log);
        var (_, token) = await SignedInAsync(app, "employee1@smartdesk.local");
        const string feedToken = "a-calendar-feed-token-nobody-may-read";

        (await app.CreateClient().GetAsync($"/api/calendar/feed.ics?token={feedToken}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await app.CreateClient().PostAsync($"/hubs/reservations/negotiate?negotiateVersion=1&access_token={token}", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await log.RequestAsync("GET", "/api/calendar/feed.ics", 404);
        await log.RequestAsync("POST", "/hubs/reservations/negotiate", 200);
        log.Text.Should().NotContain(feedToken).And.NotContain(token);
    }

    [Fact]
    public async Task Health_checks_answer_without_a_line_each()
    {
        var log = new LogLines();
        await using var app = AppWritingTo(log);
        var client = app.CreateClient();

        (await client.GetStringAsync("/health/live")).Should().Be("Healthy");
        (await client.GetStringAsync("/health")).Should().Be("Healthy");
        (await client.GetAsync("/api/resources")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await log.RequestAsync("GET", "/api/resources", 401);
        log.Requests.Should().NotContain(line => line.Properties["RequestPath"].ToString().Contains("/health"));
    }

    private WebApplicationFactory<Program> AppWritingTo(LogLines log) =>
        fixture.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<ILogEventSink>(log)));

    private static async Task<(HttpClient Client, string Token)> SignedInAsync(
        WebApplicationFactory<Program> app, string email)
    {
        var response = await app.CreateClient().PostAsJsonAsync(
            "/api/auth/login", new { email, password = IdentityDataSeeder.DemoPassword });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginBody>())!;
        var client = app.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return (client, login.AccessToken);
    }

    // Every line the API writes, as Serilog has it: message, level and fields.
    private sealed class LogLines : ILogEventSink
    {
        private readonly ConcurrentQueue<LogEvent> _lines = new();

        public void Emit(LogEvent logEvent) => _lines.Enqueue(logEvent);

        public IEnumerable<LogEvent> Requests => _lines.Where(line => line.Properties.ContainsKey("RequestMethod"));

        // All of it as text: each message, each field and each exception.
        public string Text => string.Join(
            Environment.NewLine,
            _lines.Select(line =>
                $"{line.RenderMessage()} {string.Join(" ", line.Properties.Select(p => $"{p.Key}={p.Value}"))} {line.Exception}"));

        // A request's line is written as its response ends, which can be just after the client
        // has it.
        public async Task<LogEvent> RequestAsync(string method, string path, int status)
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var line = Requests.FirstOrDefault(line =>
                    Is(line, "RequestMethod", method) && Is(line, "RequestPath", path) && Is(line, "StatusCode", status));
                if (line is not null)
                {
                    return line;
                }

                await Task.Delay(100);
            }

            throw new Xunit.Sdk.XunitException(
                $"No line for {method} {path} {status} among: {string.Join(" / ", Requests.Select(r => r.RenderMessage()))}");
        }

        private static bool Is(LogEvent line, string property, object value) =>
            line.Properties.TryGetValue(property, out var field) && field is ScalarValue { Value: var actual } && Equals(actual, value);
    }

    private sealed record LoginBody(string AccessToken);

    private sealed record Me(Guid Id);
}
