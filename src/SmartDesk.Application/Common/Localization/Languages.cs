using System.Globalization;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Enums;

namespace SmartDesk.Application.Common.Localization;

// The languages SmartDesk answers in (ADR-0023). The API sets each request's UI culture from
// its Accept-Language header, and every message is looked up in that culture, so a handler just
// uses Messages. Emails are the exception: they're written in the recipient's saved language,
// since the request that sends one is usually someone else's.
public static class Languages
{
    public const string English = "en";
    public const string Turkish = "tr";

    public static IReadOnlyList<string> Supported { get; } = [English, Turkish];

    // The current request's language: its UI culture when we speak it, English otherwise.
    public static string Current => Normalize(CultureInfo.CurrentUICulture.Name) ?? English;

    // "tr", "tr-TR" or "TR" is "tr"; a language we don't speak is null.
    public static string? Normalize(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var code = language.Split('-', '_')[0];
        return Supported.FirstOrDefault(supported => string.Equals(supported, code, StringComparison.OrdinalIgnoreCase));
    }

    // The culture to write to someone in: their saved language, or failing that the request's.
    public static CultureInfo CultureFor(string? savedLanguage) =>
        CultureInfo.GetCultureInfo(Normalize(savedLanguage) ?? Current);

    // A resource status as a message shows it: "Maintenance", "Bakımda".
    public static string Describe(ResourceStatus status) =>
        Messages.ResourceManager.GetString($"Status_{status}") ?? status.ToString();
}
