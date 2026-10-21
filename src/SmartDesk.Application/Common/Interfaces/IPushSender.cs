using SmartDesk.Application.Features.Push.Common;

namespace SmartDesk.Application.Common.Interfaces;

// Notifications on the phones and browsers people turned them on in (ADR-0056). Queue hands a notice
// to the background sender and returns at once, like IEmailSender: a request never waits on a push
// service. Off until the server has its VAPID keys: PublicKey is null and Queue keeps nothing.
public interface IPushSender
{
    // The key a browser subscribes with (applicationServerKey), base64url; null while off.
    string? PublicKey { get; }

    void Queue(PushNotice notice);
}
