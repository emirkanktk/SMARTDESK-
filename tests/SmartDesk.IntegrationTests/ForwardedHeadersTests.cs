using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace SmartDesk.IntegrationTests;

// Behind a reverse proxy (ADR-0028): the sign-in rate limit counts each client behind a trusted
// proxy separately, and doesn't believe forwarded addresses from anyone else.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class ForwardedHeadersTests(PostgresApiFixture fixture)
{
    [Fact]
    public async Task Behind_a_trusted_proxy_each_client_has_its_own_sign_in_limit()
    {
        // A proxy on this machine, which is trusted without any setting.
        await using var app = HostReachedFrom(IPAddress.Loopback);

        (await SignInAsync(app, "203.0.113.10")).Should().Be(HttpStatusCode.Unauthorized);
        (await SignInAsync(app, "203.0.113.10")).Should().Be(HttpStatusCode.Unauthorized);
        (await SignInAsync(app, "203.0.113.10")).Should().Be(HttpStatusCode.TooManyRequests);
        (await SignInAsync(app, "203.0.113.20")).Should().Be(HttpStatusCode.Unauthorized, "another client behind the proxy has a limit of its own");
    }

    [Fact]
    public async Task A_forwarded_address_from_anyone_else_is_ignored()
    {
        // Not a known proxy: its X-Forwarded-For could say anything.
        await using var app = HostReachedFrom(IPAddress.Parse("198.51.100.7"));

        (await SignInAsync(app, "203.0.113.10")).Should().Be(HttpStatusCode.Unauthorized);
        (await SignInAsync(app, "203.0.113.11")).Should().Be(HttpStatusCode.Unauthorized);
        (await SignInAsync(app, "203.0.113.12")).Should().Be(HttpStatusCode.TooManyRequests, "a made-up address doesn't buy a fresh limit");
    }

    [Fact]
    public async Task A_proxy_listed_in_the_settings_is_trusted()
    {
        await using var app = HostReachedFrom(IPAddress.Parse("10.1.2.3"), ("ForwardedHeaders:KnownNetworks:0", "10.0.0.0/8"));

        (await SignInAsync(app, "203.0.113.10")).Should().Be(HttpStatusCode.Unauthorized);
        (await SignInAsync(app, "203.0.113.10")).Should().Be(HttpStatusCode.Unauthorized);
        (await SignInAsync(app, "203.0.113.20")).Should().Be(HttpStatusCode.Unauthorized);
    }

    // The API with a sign-in limit of 2 a minute, whose connections all come from `peer`.
    private WebApplicationFactory<Program> HostReachedFrom(
        IPAddress peer, params (string Key, string Value)[] settings) =>
        fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("RateLimiting:LoginPermitsPerMinute", "2");
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureServices(services => services.AddTransient<IStartupFilter>(_ => new ConnectionFrom(peer)));
        });

    private static async Task<HttpStatusCode> SignInAsync(
        WebApplicationFactory<Program> app, string client)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email = "nobody@integration.example", password = "Not-The-Password-1" }),
        };
        request.Headers.Add("X-Forwarded-For", client);
        using var response = await app.CreateClient().SendAsync(request);
        return response.StatusCode;
    }

    // The test server has no network connection; this gives each request the peer address a
    // real one would have.
    private sealed class ConnectionFrom(IPAddress peer) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                context.Connection.RemoteIpAddress = peer;
                return nextMiddleware(context);
            });
            next(app);
        };
    }
}
