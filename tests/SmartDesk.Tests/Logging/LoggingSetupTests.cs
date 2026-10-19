using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using SmartDesk.API.Logging;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Logging;

// Structured logs (ADR-0053): how loud a request's line is, and that every line written while a
// request runs says whose it was.
public class LoggingSetupTests
{
    [Theory]
    [InlineData("/api/reservations/mine", 200, false, LogEventLevel.Information)]
    [InlineData("/api/reservations", 409, false, LogEventLevel.Information)]
    [InlineData("/api/reservations", 500, false, LogEventLevel.Error)]
    [InlineData("/api/reservations", 200, true, LogEventLevel.Error)]
    [InlineData("/health", 200, false, LogEventLevel.Verbose)]
    [InlineData("/health/live", 200, false, LogEventLevel.Verbose)]
    [InlineData("/health", 503, false, LogEventLevel.Error)]
    [InlineData("/healthy-looking", 200, false, LogEventLevel.Information)]
    public void A_request_is_logged_at_its_level(string path, int status, bool failed, LogEventLevel level)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;

        LoggingSetup.LevelOf(context, 12.5, failed ? new InvalidOperationException() : null).Should().Be(level);
    }

    [Fact]
    public async Task Every_line_written_during_a_request_says_whose_it_was()
    {
        var lines = new List<LogEvent>();
        var logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.Sink(new Collect(lines)).CreateLogger();
        var ayse = Guid.NewGuid();

        await RequestAsync(new FakeCurrentUser(ayse), logger);
        await RequestAsync(new FakeCurrentUser(null), logger);
        logger.Information("Released 2 booking(s) nobody checked in to");

        lines.Should().HaveCount(3);
        lines[0].Properties[LoggingSetup.UserIdProperty].Should().Be(new ScalarValue(ayse));
        lines[1].Properties.Should().NotContainKey(LoggingSetup.UserIdProperty, "nobody signed in");
        lines[2].Properties.Should().NotContainKey(LoggingSetup.UserIdProperty, "the request is over");
    }

    // A request through UseUserInLogs to an endpoint that writes a line.
    private static async Task RequestAsync(ICurrentUserService user, Serilog.ILogger logger)
    {
        var services = new ServiceCollection().AddSingleton(user).BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        app.UseUserInLogs();
        app.Run(_ =>
        {
            logger.Information("Booked {ResourceName}", "Board Room");
            return Task.CompletedTask;
        });

        await app.Build()(new DefaultHttpContext { RequestServices = services });
    }

    private sealed class Collect(List<LogEvent> lines) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => lines.Add(logEvent);
    }
}
