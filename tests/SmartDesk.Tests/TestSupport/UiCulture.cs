using System.Globalization;
using System.Runtime.CompilerServices;

namespace SmartDesk.Tests.TestSupport;

// Messages follow the UI culture (ADR-0023). The API sets it for every request; tests call
// handlers directly, so they'd otherwise get the machine's language. English unless a test
// asks for another with UiCulture.Use.
public sealed class UiCulture : IDisposable
{
    private readonly CultureInfo _previous = CultureInfo.CurrentUICulture;

    private UiCulture(string name) => CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);

    public static UiCulture Use(string name) => new(name);

    public void Dispose() => CultureInfo.CurrentUICulture = _previous;

    [ModuleInitializer]
    internal static void EnglishByDefault() =>
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("en-US");
}
