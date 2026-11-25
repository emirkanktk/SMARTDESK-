using FluentAssertions;
using Microsoft.AspNetCore.Http;
using SmartDesk.API.Authentication;
using SmartDesk.Application.Features.Auth.Commands.Login;
using SmartDesk.Application.Features.Auth.Common;

namespace SmartDesk.Tests.Auth;

// The session cookie (ADR-0026): written with every new refresh token, and left alone by an
// answer that has none - the renewal that another with the same token beat by a moment.
public sealed class SessionCookieTests
{
    private static readonly DateTime Expires = new(2026, 10, 19, 9, 0, 0, DateTimeKind.Utc);

    private static SignedIn Answer(string? refreshToken) =>
        new(new LoginResponse("access-token", Expires, false), refreshToken, Expires);

    [Fact]
    public void A_new_refresh_token_goes_into_a_locked_down_cookie()
    {
        var context = new DefaultHttpContext();

        SessionCookie.Write(context.Response, Answer("refresh-token"));

        var cookie = context.Response.Headers.SetCookie.Single()!;
        cookie.Should().StartWith($"{SessionCookie.Name}=refresh-token;");
        cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure")
            .And.Contain("samesite=strict").And.Contain("path=/api/auth");
        context.Response.Headers.CacheControl.ToString().Should().Be("no-store");
    }

    [Fact]
    public void An_answer_without_a_refresh_token_leaves_the_cookie_alone()
    {
        var context = new DefaultHttpContext();

        SessionCookie.Write(context.Response, Answer(null));

        context.Response.Headers.SetCookie.Should().BeEmpty("the browser keeps the token another answer brought");
        context.Response.Headers.CacheControl.ToString().Should().Be("no-store", "it still carries an access token");
    }

    // ADR-0062: a sign-in also says which browser this is, in a cookie that outlives the session.
    [Fact]
    public void A_sign_ins_device_token_goes_into_a_locked_down_cookie_kept_for_400_days()
    {
        var context = new DefaultHttpContext();

        SessionCookie.Write(context.Response, Answer("refresh-token") with { DeviceToken = "device-token" });

        var cookie = context.Response.Headers.SetCookie.Single(c => c!.StartsWith(DeviceCookie.Name + "=", StringComparison.Ordinal))!;
        cookie.Should().StartWith($"{DeviceCookie.Name}=device-token;");
        cookie.ToLowerInvariant().Should().Contain("httponly").And.Contain("secure")
            .And.Contain("samesite=strict").And.Contain("path=/api/auth").And.Contain("max-age=34560000");
    }

    [Fact]
    public void A_renewal_or_a_password_change_leaves_the_device_cookie_alone()
    {
        var context = new DefaultHttpContext();

        SessionCookie.Write(context.Response, Answer("refresh-token"));

        context.Response.Headers.SetCookie.Should().ContainSingle().Which.Should().StartWith($"{SessionCookie.Name}=");
    }
}
