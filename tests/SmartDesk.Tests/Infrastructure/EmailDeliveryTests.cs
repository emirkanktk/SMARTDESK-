using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Features.Auth.Common;
using SmartDesk.Infrastructure;
using SmartDesk.Infrastructure.Email;

namespace SmartDesk.Tests.Infrastructure;

public class EmailDeliveryTests
{
    private static EmailMessage Message(int n = 1) => new($"user{n}@smartdesk.local", $"Subject {n}", "text", "<p>html</p>");

    [Fact]
    public async Task TheWorker_SendsWhatTheQueueCollects()
    {
        var queue = new EmailQueue(NullLogger<EmailQueue>.Instance);
        var transport = new FakeTransport();
        using var worker = new EmailDispatchWorker(queue, transport, TimeProvider.System, NullLogger<EmailDispatchWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        queue.Queue(Message(1));
        queue.Queue(Message(2));
        await transport.WaitForAsync(2);
        await worker.StopAsync(CancellationToken.None);

        transport.Delivered.Select(m => m.To).Should().Equal("user1@smartdesk.local", "user2@smartdesk.local");
    }

    [Fact]
    public async Task AFailedSend_IsRetriedAfterAPause_ThenDelivered()
    {
        var queue = new EmailQueue(NullLogger<EmailQueue>.Instance);
        var transport = new FakeTransport { FailuresBeforeSuccess = 2 };
        var clock = new FakeTimeProvider();
        using var worker = new EmailDispatchWorker(queue, transport, clock, NullLogger<EmailDispatchWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        queue.Queue(Message());
        await transport.WaitForAttemptsAsync(1);
        transport.Delivered.Should().BeEmpty(); // no retry before the pause has passed
        await transport.AdvanceUntilAsync(clock, () => transport.Delivered.Count == 1);
        await worker.StopAsync(CancellationToken.None);

        transport.Attempts.Should().Be(3);
    }

    [Fact]
    public async Task AMessageThatKeepsFailing_IsGivenUp_AndTheNextOneStillGoesOut()
    {
        var queue = new EmailQueue(NullLogger<EmailQueue>.Instance);
        var transport = new FakeTransport { FailFor = "user1@smartdesk.local" };
        var clock = new FakeTimeProvider();
        using var worker = new EmailDispatchWorker(queue, transport, clock, NullLogger<EmailDispatchWorker>.Instance);

        await worker.StartAsync(CancellationToken.None);
        queue.Queue(Message(1));
        queue.Queue(Message(2));
        await transport.AdvanceUntilAsync(clock, () => transport.Delivered.Count == 1);
        await worker.StopAsync(CancellationToken.None);

        transport.Delivered.Select(m => m.To).Should().Equal("user2@smartdesk.local");
        transport.Attempts.Should().Be(1 + EmailDispatchWorker.RetryDelays.Length + 1); // 3 for user1, 1 for user2
    }

    [Fact]
    public void AFullQueue_DropsNewMessagesInsteadOfBlocking()
    {
        var queue = new EmailQueue(NullLogger<EmailQueue>.Instance);

        for (var i = 0; i <= EmailQueue.Capacity; i++)
        {
            queue.Queue(Message(i));
        }

        queue.Reader.Count.Should().Be(EmailQueue.Capacity);
    }

    [Fact]
    public void Links_PointAtTheFrontend_WithTheEmailAndTokenEscaped()
    {
        var links = new FrontendLinks(Options.Create(new AppOptions { FrontendUrl = "https://desk.example.com/" }));

        links.ResetPassword("ayse+test@example.com", "abc-_123").Should()
            .Be("https://desk.example.com/reset-password?email=ayse%2Btest%40example.com&token=abc-_123");
        links.SignIn().Should().Be("https://desk.example.com/login");
    }

    [Theory]
    [InlineData("http://localhost:4200", true)]
    [InlineData("https://desk.example.com", true)]
    [InlineData("", false)]
    [InlineData("localhost:4200", false)]
    [InlineData("ftp://desk.example.com", false)]
    public void TheFrontendUrl_MustBeAnAbsoluteHttpAddress(string url, bool valid) =>
        new AppOptions { FrontendUrl = url }.HasValidFrontendUrl.Should().Be(valid);

    [Fact]
    public void Infrastructure_WiresEmailAndResetLinks_AsTheAppUsesThem()
    {
        using var logOnly = Build(smtpHost: null);
        using var smtp = Build(smtpHost: "localhost");

        logOnly.GetRequiredService<IEmailTransport>().Should().BeOfType<LoggingEmailTransport>();
        smtp.GetRequiredService<IEmailTransport>().Should().BeOfType<SmtpEmailTransport>();
        smtp.GetRequiredService<IEmailSender>().Should().BeSameAs(smtp.GetRequiredService<EmailQueue>());
        smtp.GetRequiredService<IOptions<DataProtectionTokenProviderOptions>>().Value.TokenLifespan
            .Should().Be(AccountEmails.ResetLinkLifetime);
    }

    [Fact]
    public void Infrastructure_RefusesToStartWithoutAFrontendUrl()
    {
        using var services = Build(smtpHost: null, frontendUrl: null);

        var act = () => services.GetRequiredService<IOptions<AppOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*App:FrontendUrl*");
    }

    private static ServiceProvider Build(string? smtpHost, string? frontendUrl = "http://localhost:4200")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused",
            ["Jwt:Issuer"] = "SmartDesk",
            ["Jwt:Audience"] = "SmartDesk",
            ["Jwt:Key"] = new string('k', 32),
            ["App:FrontendUrl"] = frontendUrl,
            ["Email:Smtp:Host"] = smtpHost,
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    private sealed class FakeTransport : IEmailTransport
    {
        private readonly object _gate = new();
        public List<EmailMessage> Delivered { get; } = [];
        public int Attempts { get; private set; }
        public int FailuresBeforeSuccess { get; init; }
        public string? FailFor { get; init; }

        public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                Attempts++;
                if (message.To == FailFor || Attempts <= FailuresBeforeSuccess)
                {
                    throw new IOException("SMTP server unavailable");
                }

                Delivered.Add(message);
            }

            return Task.CompletedTask;
        }

        public Task WaitForAsync(int delivered) => Until(() => Delivered.Count >= delivered);

        public Task WaitForAttemptsAsync(int attempts) => Until(() => Attempts >= attempts);

        // Moves the fake clock forward in small steps until the condition holds. Stepping,
        // rather than advancing once, can't race the worker registering its next delay.
        public async Task AdvanceUntilAsync(FakeTimeProvider clock, Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (_gate)
                {
                    if (condition()) return;
                }

                clock.Advance(TimeSpan.FromSeconds(1));
                await Task.Delay(5);
            }

            throw new TimeoutException("The worker didn't get there in time.");
        }

        private async Task Until(Func<bool> condition)
        {
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (_gate)
                {
                    if (condition()) return;
                }

                await Task.Delay(10);
            }

            throw new TimeoutException("The worker didn't get there in time.");
        }
    }
}
