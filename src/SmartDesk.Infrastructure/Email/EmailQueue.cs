using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Infrastructure.Email;

// IEmailSender for handlers: puts the message on an in-memory queue and returns (ADR-0019).
// Bounded, so a flood of requests can't grow memory without limit; a full queue drops the
// new message with a warning rather than blocking the request.
public sealed class EmailQueue(ILogger<EmailQueue> logger) : IEmailSender
{
    public const int Capacity = 1000;

    private readonly Channel<EmailMessage> _channel = Channel.CreateBounded<EmailMessage>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public ChannelReader<EmailMessage> Reader => _channel.Reader;

    public void Queue(EmailMessage message)
    {
        if (!_channel.Writer.TryWrite(message))
        {
            logger.LogWarning("Email queue is full; dropped \"{Subject}\" to {To}", message.Subject, message.To);
        }
    }
}

// Sends what EmailQueue collects, one message at a time. A transient failure (the server is
// restarting, a network blip) gets two more tries a few seconds apart; after that the
// message is logged as lost and the next one goes out.
public sealed class EmailDispatchWorker(
    EmailQueue queue,
    IEmailTransport transport,
    TimeProvider timeProvider,
    ILogger<EmailDispatchWorker> logger) : BackgroundService
{
    public static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
        {
            await SendWithRetriesAsync(message, stoppingToken);
        }
    }

    private async Task SendWithRetriesAsync(EmailMessage message, CancellationToken stoppingToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await transport.SendAsync(message, stoppingToken);
                logger.LogInformation("Sent \"{Subject}\" to {To}", message.Subject, message.To);
                return;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                if (attempt >= RetryDelays.Length)
                {
                    logger.LogError(exception, "Could not send \"{Subject}\" to {To}; giving up", message.Subject, message.To);
                    return;
                }

                logger.LogWarning(exception, "Sending \"{Subject}\" to {To} failed; trying again", message.Subject, message.To);
                await Task.Delay(RetryDelays[attempt], timeProvider, stoppingToken);
            }
        }
    }
}
