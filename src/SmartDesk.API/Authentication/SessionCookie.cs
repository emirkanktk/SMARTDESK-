using SmartDesk.Application.Features.Auth.Common;

namespace SmartDesk.API.Authentication;

// The session's refresh token travels only in this cookie (ADR-0026):
// - HttpOnly, so no script on the page - an injected one included - can read it
// - Secure, so it's never sent in the clear
// - SameSite=Strict and scoped to /api/auth, so it goes to nothing but the renew and sign-out
//   calls, and never with a request another site starts
// The frontend is served from the same site as the API (the dev server proxies /api), so the
// browser sends it without any CORS credentials setup.
public static class SessionCookie
{
    public const string Name = "smartdesk_session";
    public const string Path = "/api/auth";

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Write(HttpResponse response, SignedIn signedIn)
    {
        response.Cookies.Append(Name, signedIn.RefreshToken, Options(signedIn.RefreshExpiresAt));

        // A response with a token in it is never kept by a browser or proxy cache.
        response.Headers.CacheControl = "no-store";
    }

    public static void Clear(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    private static CookieOptions Options(DateTime? expiresAt) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        Expires = expiresAt,
        IsEssential = true,
    };
}
