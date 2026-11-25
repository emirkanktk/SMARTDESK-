using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
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

    private static string SetCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith(CookieName + "=", StringComparison.Ordinal));

    private static string CookieValue(string setCookie) => setCookie[(CookieName.Length + 1)..].Split(';')[0];

    private sealed record LoginBody(string AccessToken, DateTime ExpiresAt);

    private sealed record SessionBody(Guid Id, string? UserAgent, string? Method, DateTime StartedAt, DateTime LastActiveAt, bool Current);

    private sealed record EndedBody(int Ended);
}
