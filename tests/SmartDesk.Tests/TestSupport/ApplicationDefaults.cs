using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Application;

namespace SmartDesk.Tests.TestSupport;

// AddApplication() also sets FluentValidation's global options: messages in the UI culture,
// field names from Messages ("Ad") (ADR-0023). The API calls it at startup. Here it runs
// before any test, so a test that builds a validator directly doesn't depend on another test
// having called it first (#74).
internal static class ApplicationDefaults
{
    [ModuleInitializer]
    internal static void Apply() => new ServiceCollection().AddApplication();
}
