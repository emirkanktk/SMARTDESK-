using System.Text.RegularExpressions;

namespace SmartDesk.Application.Common.Localization;

// A browser's description of itself (its User-Agent) in the words a person uses, "Safari ·
// iPhone", for the notice of a sign-in on a new device (ADR-0062). The rules are those of the
// app's list of signed-in devices (frontend/src/app/core/utils/user-agent.ts, ADR-0061), so the
// notice names a device the way the list it points to does: a change to one goes to the other.
public static class DeviceNames
{
    private const RegexOptions Options = RegexOptions.CultureInvariant;

    // In this order: Edge, Opera and Samsung Internet name Chrome too, and Chrome names Safari. On an
    // iPhone each browser has a word of its own (CriOS, FxiOS, EdgiOS) before Safari's.
    private static readonly (Regex Pattern, string Name)[] Browsers =
    [
        (new Regex(@"\bEdg(?:e|A|iOS)?/", Options), "Edge"),
        (new Regex(@"\b(?:OPR|OPiOS)/", Options), "Opera"),
        (new Regex(@"\bSamsungBrowser/", Options), "Samsung Internet"),
        (new Regex(@"\b(?:Firefox|FxiOS)/", Options), "Firefox"),
        (new Regex(@"\b(?:Chrome|CriOS|HeadlessChrome)/", Options), "Chrome"),
        (new Regex(@"\bVersion/[0-9.]+.*\bSafari/", Options), "Safari"),
    ];

    // An iPhone says "like Mac OS X" and Android says Linux, so they come first. An iPad set to show
    // sites as on a computer says it's a Mac; its User-Agent alone can't tell.
    private static readonly (Regex Pattern, string Name)[] Systems =
    [
        (new Regex(@"\biPhone\b", Options), "iPhone"),
        (new Regex(@"\biPad\b", Options), "iPad"),
        (new Regex(@"\bAndroid\b", Options), "Android"),
        (new Regex(@"\bWindows\b", Options), "Windows"),
        (new Regex(@"\bCrOS\b", Options), "ChromeOS"),
        (new Regex(@"\b(?:Macintosh|Mac OS X)\b", Options), "Mac"),
        (new Regex(@"\bLinux\b", Options), "Linux"),
    ];

    // "Safari · iPhone": the browser and what it runs on, whichever of them it can tell; null when
    // it can tell neither.
    public static string? Describe(string? userAgent)
    {
        var agent = userAgent ?? string.Empty;
        var known = new[] { FirstMatch(Browsers, agent), FirstMatch(Systems, agent) }.OfType<string>().ToList();
        return known.Count == 0 ? null : string.Join(" · ", known);
    }

    private static string? FirstMatch((Regex Pattern, string Name)[] candidates, string agent) =>
        candidates.FirstOrDefault(c => c.Pattern.IsMatch(agent)).Name;
}
