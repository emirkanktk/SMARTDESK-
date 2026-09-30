using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// The assistant's endpoints (ADR-0029). Without Assistant:ApiKey it's off and says so. With a
// model - a script here, never the paid API - it looks things up as the caller and only
// proposes: the caller books through the reservation endpoint.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class AssistantTests(PostgresApiFixture fixture)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Without_a_key_the_assistant_is_off()
    {
        var client = await SignedInAsync(fixture, "employee1@smartdesk.local");

        var status = await client.GetFromJsonAsync<JsonElement>("/api/assistant");
        var response = await AskAsync(client, "Is Board Room free tomorrow at 10?");

        status.GetProperty("enabled").GetBoolean().Should().BeFalse();
        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("detail").GetString().Should().Be("The assistant isn't set up on this server.");
    }

    [Fact]
    public async Task Only_signed_in_users_reach_it()
    {
        var client = fixture.CreateClient();

        (await client.GetAsync("/api/assistant")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await AskAsync(client, "Hi")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task It_proposes_and_only_the_callers_own_request_books()
    {
        var day = DateTime.UtcNow.Date.AddDays(130);
        var local = day.ToString("yyyy-MM-dd");
        await using var app = WithModel(async toolbox =>
        {
            using var found = JsonDocument.Parse(await toolbox.RunAsync(
                "find_resources", JsonSerializer.SerializeToElement(new { search = "board room" }), CancellationToken.None));
            var id = found.RootElement.GetProperty("resources")[0].GetProperty("id").GetString();
            await toolbox.RunAsync("propose_booking",
                JsonSerializer.SerializeToElement(new { resourceId = id, start = $"{local}T10:00", end = $"{local}T11:00" }),
                CancellationToken.None);
            return "Board Room is free then. The card is ready.";
        });
        var client = await SignedInAsync(app, "employee1@smartdesk.local");

        var response = await AskAsync(client, $"Board Room on {local} at 10, please.", "Europe/Istanbul");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var reply = (await response.Content.ReadFromJsonAsync<AssistantReply>(Json))!;
        reply.Reply.Should().Be("Board Room is free then. The card is ready.");
        var card = reply.Proposals.Should().ContainSingle().Subject;
        card.Kind.Should().Be("book");
        card.ResourceName.Should().Be("Board Room");
        // 10:00 in Istanbul, UTC+3.
        card.StartDateTime.Should().Be(day.AddHours(7));
        card.EndDateTime.Should().Be(day.AddHours(8));

        var mine = await client.GetFromJsonAsync<List<Booked>>("/api/reservations/mine", Json);
        mine!.Should().NotContain(r => r.ResourceId == card.ResourceId && r.StartDateTime == card.StartDateTime, "a proposal books nothing");

        var booked = await client.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = card.ResourceId,
            startDateTime = card.StartDateTime,
            endDateTime = card.EndDateTime,
        });
        booked.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Each_user_has_a_limit_of_their_own()
    {
        await using var app = WithModel(_ => Task.FromResult("Hello!"), ("RateLimiting:AssistantPermitsPerMinute", "2"));
        var ayse = await SignedInAsync(app, "employee1@smartdesk.local");
        var zeynep = await SignedInAsync(app, "manager@smartdesk.local");

        (await AskAsync(ayse, "Hi")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AskAsync(ayse, "Hi")).StatusCode.Should().Be(HttpStatusCode.OK);
        var limited = await AskAsync(ayse, "Hi");
        var someoneElse = await AskAsync(zeynep, "Hi");

        limited.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        limited.Headers.RetryAfter.Should().NotBeNull();
        using var problem = JsonDocument.Parse(await limited.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("detail").GetString()
            .Should().Be("You've asked the assistant a lot in the last minute. Wait a moment and try again.");
        someoneElse.StatusCode.Should().Be(HttpStatusCode.OK, "another user has a limit of their own");
    }

    // The API with a scripted model in place of Claude, and any extra settings.
    private WebApplicationFactory<Program> WithModel(
        Func<IAssistantToolbox, Task<string>> script, params (string Key, string Value)[] settings) =>
        fixture.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services => services.AddSingleton<IAssistantModel>(new ScriptedModel(script)));
        });

    private static Task<HttpResponseMessage> AskAsync(HttpClient client, string text, string? timeZone = null) =>
        client.PostAsJsonAsync("/api/assistant/messages", new
        {
            messages = new[] { new { role = "user", text } },
            timeZone,
        });

    private static async Task<HttpClient> SignedInAsync(WebApplicationFactory<Program> app, string email)
    {
        var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password = IdentityDataSeeder.DemoPassword });
        response.EnsureSuccessStatusCode();
        var login = (await response.Content.ReadFromJsonAsync<LoginBody>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", login.AccessToken);
        return client;
    }

    private sealed class ScriptedModel(Func<IAssistantToolbox, Task<string>> script) : IAssistantModel
    {
        public bool IsConfigured => true;

        public Task<string> AnswerAsync(AssistantRequest request, IAssistantToolbox toolbox, CancellationToken cancellationToken) =>
            script(toolbox);
    }

    private sealed record AssistantReply(string Reply, List<Card> Proposals);

    private sealed record Card(string Kind, Guid? ResourceId, Guid? ReservationId, string ResourceName, DateTime StartDateTime, DateTime EndDateTime, string? Note);

    private sealed record Booked(Guid ResourceId, DateTime StartDateTime);

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAt);
}
