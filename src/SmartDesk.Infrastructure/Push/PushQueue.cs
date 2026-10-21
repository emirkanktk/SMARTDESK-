using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Push.Common;

namespace SmartDesk.Infrastructure.Push;

// IPushSender for handlers and notifiers (ADR-0056): puts the notice on an in-memory queue and
// returns, like EmailQueue (ADR-0019). Bounded, so a flood can't grow memory without limit; a full
// queue drops the new notice with a warning. While the server has no VAPID keys it keeps nothing.
public sealed class PushQueue(IOptions<WebPushOptions> options, ILogger<PushQueue> logger) : IPushSender
{
    public const int Capacity = 1000;

    private readonly Channel<PushNotice> _channel = Channel.CreateBounded<PushNotice>(
        new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

    public ChannelReader<PushNotice> Reader => _channel.Reader;

    public string? PublicKey => options.Value.IsConfigured ? options.Value.PublicKey : null;

    public void Queue(PushNotice notice)
    {
        if (PublicKey is null || notice.RecipientIds.Count == 0)
        {
            return;
        }

        if (!_channel.Writer.TryWrite(notice))
        {
            logger.LogWarning("Push queue is full; dropped a {Topic} notice", notice.Topic);
        }
    }
}
