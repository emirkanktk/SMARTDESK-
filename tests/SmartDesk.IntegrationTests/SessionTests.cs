using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// Sessions over HTTP (ADR-0026): the refresh token only ever travels in an HttpOnly cookie,
// renewing replaces it, and signing out stops the access token on the server as well.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SessionTests(PostgresApiFixture fixture)
{
    private const string CookieName = "smartdesk_session";

    [Fact]
    public async Task Sign_in_sets_a_locked_down_cookie_and_keeps_the_refresh_token_out_of_the_body()
    {
        var response = await Anonymous().PostAsJsonAsync("/api/auth/login", new { email = "employee2@smartdesk.local", password = IdentityDataSeeder.DemoPassword });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var cookie = SetCookie(response);
        cookie.Should().StartWith($"{CookieName}=");
        cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict").And.Contain("path=/api/auth");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("accessToken").And.NotContain(CookieValue(cookie));
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Renewing_replaces_the_cookie_and_signing_out_stops_the_access_token()
    {
        var login = await Anonymous().PostAsJsonAsync("/api/auth/login", new { email = "employee2@smartdesk.local", password = IdentityDataSeeder.DemoPassword });
        var firstCookie = CookieValue(SetCookie(login));
        var accessToken = (await login.Content.ReadFromJsonAsync<LoginBody>())!.AccessToken;

        var renewed = await WithCookie(firstCookie).PostAsync("/api/auth/refresh", null);
        renewed.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondCookie = CookieValue(SetCookie(renewed));
        secondCookie.Should().NotBe(firstCookie);
        (await Bearer(accessToken).GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK, "same session, so the earlier token still works");

        var signedOut = await WithCookie(secondCookie).PostAsync("/api/auth/logout", null);

        signedOut.StatusCode.Should().Be(HttpStatusCode.NoContent);
        SetCookie(signedOut).Should().StartWith($"{CookieName}=;");
        (await Bearer(accessToken).GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "its session is over");
        (await WithCookie(secondCookie).PostAsync("/api/auth/refresh", null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_refresh_with_a_dead_cookie_is_refused_and_the_cookie_cleared()
    {
        var bogus = await WithCookie("not-a-token").PostAsync("/api/auth/refresh", null);

        bogus.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        SetCookie(bogus).Should().StartWith($"{CookieName}=;");
    }

    [Fact]
    public async Task A_guest_without_the_cookie_gets_nothing_to_renew_and_no_error()
    {
        // Every visit to a public page asks; a 401 would show up as a failed request each time.
        var none = await Anonymous().PostAsync("/api/auth/refresh", null);

        none.StatusCode.Should().Be(HttpStatusCode.NoContent);
        none.Headers.Contains("Set-Cookie").Should().BeFalse();
    }

    // ADR-0061: every sign-in is a device in its owner's list, and any of them signs another out.
    [Fact]
    public async Task The_list_names_each_device_and_one_signs_another_out_at_once()
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        var laptop = await SignInOnAsync($"Laptop browser {run}");
        var phone = await SignInOnAsync($"Phone browser {run}");

        var list = await Bearer(laptop).GetFromJsonAsync<List<SessionBody>>("/api/auth/sessions");
        var these = list!.Where(s => s.UserAgent?.EndsWith(run, StringComparison.Ordinal) == true).ToList();
        these.Select(s => (s.UserAgent, s.Method, s.Current)).Should().BeEquivalentTo(new[]
        {
            ($"Laptop browser {run}", "Password", true),
            ($"Phone browser {run}", "Password", false),
        });
        var phoneSession = these.Single(s => !s.Current).Id;

        (await Bearer(laptop).DeleteAsync($"/api/auth/sessions/{phoneSession}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Bearer(phone).GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "its session is over");
        (await Bearer(laptop).GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Bearer(laptop).DeleteAsync($"/api/auth/sessions/{phoneSession}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Anonymous().GetAsync("/api/auth/sessions")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Signing_out_everywhere_else_keeps_only_this_device()
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        var here = await SignInOnAsync($"This browser {run}");
        var elsewhere = await SignInOnAsync($"Another browser {run}");

        var ended = await Bearer(here).PostAsync("/api/auth/sessions/end-others", null);

        ended.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ended.Content.ReadFromJsonAsync<EndedBody>())!.Ended.Should().BeGreaterThanOrEqualTo(1);
        (await Bearer(elsewhere).GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var list = await Bearer(here).GetFromJsonAsync<List<SessionBody>>("/api/auth/sessions");
        list.Should().ContainSingle().Which.UserAgent.Should().Be($"This browser {run}");
    }

    // ADR-0062: a sign-in names the browser in a cookie of its own, which outlives its sessions,
    // and a sign-in on a browser the account hasn't been used on is emailed to its owner.
    [Fact]
    public async Task A_sign_in_names_the_browser_in_a_cookie_that_signing_out_leaves()
    {
        var login = await Anonymous().PostAsJsonAsync("/api/auth/login", new { email = "employee2@smartdesk.local", password = IdentityDataSeeder.DemoPassword });

        var device = SetCookie(login, DeviceCookieName);
        device.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure").And.Contain("samesite=strict")
            .And.Contain("path=/api/auth").And.Contain("max-age=34560000");
        var signedOut = await WithCookie(CookieValue(SetCookie(login))).PostAsync("/api/auth/logout", null);
        signedOut.Headers.GetValues("Set-Cookie").Should().NotContain(c => c.StartsWith(DeviceCookieName + "=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_sign_in_on_a_browser_the_account_hasnt_used_is_emailed_to_its_owner()
    {
        var emails = new EmailsSent();
        await using var host = fixture.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services => services.AddSingleton<IEmailSender>(emails)));
        var run = Guid.NewGuid().ToString("N")[..8];

        // Other tests' sign-ins are other browsers of this account's, so this one may be new too.
        var laptop = await SignInWithAsync(host, $"Laptop browser {run}", deviceToken: null);
        var laptopToken = CookieValue(SetCookie(laptop, DeviceCookieName));
        emails.Clear();

        var again = await SignInWithAsync(host, $"Laptop browser {run}", laptopToken);

        again.StatusCode.Should().Be(HttpStatusCode.OK);
        emails.To("employee2@smartdesk.local").Should().BeEmpty("it signed in on this browser before");
        CookieValue(SetCookie(again, DeviceCookieName)).Should().Be(laptopToken, "the browser keeps its token");

        var phone = await SignInWithAsync(host, $"Mozilla/5.0 (iPhone; CPU iPhone OS 26_1 like Mac OS X) Version/26.1 Mobile/15E148 Safari/604.1 {run}", deviceToken: null);

        phone.StatusCode.Should().Be(HttpStatusCode.OK);
        var notice = emails.To("employee2@smartdesk.local").Should().ContainSingle().Subject;
        notice.Subject.Should().Be("New sign-in to your SmartDesk account");
        notice.Text.Should().Contain("Safari · iPhone").And.Contain("/profile#sessions");
        CookieValue(SetCookie(phone, DeviceCookieName)).Should().NotBe(laptopToken);
    }

    private const string DeviceCookieName = "smartdesk_device";

    // In English, so the account's emails are too from the next one on (ADR-0023).
    private static async Task<HttpResponseMessage> SignInWithAsync(WebApplicationFactory<Program> host, string userAgent, string? deviceToken)
    {
        var client = host.CreateClient(new() { HandleCookies = false });
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("en");
        if (deviceToken is not null)
        {
            client.DefaultRequestHeaders.Add("Cookie", $"{DeviceCookieName}={deviceToken}");
        }

        return await client.PostAsJsonAsync("/api/auth/login", new { email = "employee2@smartdesk.local", password = IdentityDataSeeder.DemoPassword });
    }

    private sealed class EmailsSent : IEmailSender
    {
        private readonly ConcurrentQueue<EmailMessage> _sent = new();

        public void Queue(EmailMessage message) => _sent.Enqueue(message);

        public IReadOnlyList<EmailMessage> To(string address) => _sent.Where(m => m.To == address).ToList();

        public void Clear() => _sent.Clear();
    }

    private async Task<string> SignInOnAsync(string userAgent)
    {
        var client = Anonymous();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
        var login = await client.PostAsJsonAsync("/api/auth/login", new { email = "employee2@smartdesk.local", password = IdentityDataSeeder.DemoPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await login.Content.ReadFromJsonAsync<LoginBody>())!.AccessToken;
    }

    // The test server speaks plain http, where a client wouldn't send a Secure cookie back, so
    // the cookie is read and sent by hand.
    private HttpClient Anonymous() => fixture.CreateClient(new() { HandleCookies = false });

    private HttpClient WithCookie(string value)
    {
        var client = Anonymous();
        client.DefaultRequestHeaders.Add("Cookie", $"{CookieName}={value}");
        return client;
    }

    private HttpClient Bearer(string token)
    {
        var client = Anonymous();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string SetCookie(HttpResponseMessage response, string name = CookieName) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(name + "=", StringComparison.Ordinal));

    private static string CookieValue(string setCookie) => setCookie[(setCookie.IndexOf('=') + 1)..].Split(';')[0];

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAt);

    private sealed record SessionBody(Guid Id, string? UserAgent, string? Method, DateTime StartedAt, DateTime LastActiveAt, bool Current);

    private sealed record EndedBody(int Ended);
}
