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
}
