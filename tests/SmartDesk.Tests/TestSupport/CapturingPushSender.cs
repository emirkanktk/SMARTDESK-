using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Features.Push.Common;

namespace SmartDesk.Tests.TestSupport;

// IPushSender that keeps what it's given (ADR-0056). On, with a key of its own, unless made off.
public sealed class CapturingPushSender(bool on = true) : IPushSender
{
    public List<PushNotice> Queued { get; } = [];

    public string? PublicKey { get; } = on ? "BTestKey" : null;

    public void Queue(PushNotice notice) => Queued.Add(notice);
}
