using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.API.Authentication;

// The token a browser is known by to the accounts that signed in on it (ADR-0062). Locked down
// like the session's cookie (ADR-0026) - HttpOnly, Secure, SameSite=Strict, and sent only to
// /api/auth, where signing in is - but it outlives every session: signing out leaves it, so the
// next sign-in on this browser is one the account knows. Each sign-in writes it again, so it
// lasts as long as the server remembers the browser after its latest one.
public static class DeviceCookie
{
    public const string Name = "smartdesk_device";

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Write(HttpResponse response, string token) =>
        response.Cookies.Append(Name, token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = SessionCookie.Path,
            MaxAge = IKnownDevices.RememberedFor,
            IsEssential = true,
        });
}
