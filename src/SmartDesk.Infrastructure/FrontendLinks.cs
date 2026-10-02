using Microsoft.Extensions.Options;
using SmartDesk.Application.Common.Interfaces;

namespace SmartDesk.Infrastructure;

// App:FrontendUrl, e.g. "http://localhost:4200" - where the Angular app is served. Required:
// without it every emailed link would point nowhere, so startup fails instead.
public sealed class AppOptions
{
    public const string SectionName = "App";

    public string FrontendUrl { get; init; } = string.Empty;

    public bool HasValidFrontendUrl =>
        Uri.TryCreate(FrontendUrl, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}

public sealed class FrontendLinks(IOptions<AppOptions> options) : IFrontendLinks
{
    private string Base => options.Value.FrontendUrl.TrimEnd('/');

    public string CheckIn(Guid resourceId, string code) =>
        $"{Base}/check-in/{resourceId}?code={Uri.EscapeDataString(code)}";

    public string ResetPassword(string email, string token) =>
        $"{Base}/reset-password?email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";

    public string SignIn() => $"{Base}/login";

    public string ForgotPassword() => $"{Base}/forgot-password";

    public string Users() => $"{Base}/admin/users";

    public string Team() => $"{Base}/team";
}
