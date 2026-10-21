using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Localization;
using SmartDesk.Application.Features.Push.Common;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Enums;
using SmartDesk.Infrastructure.Persistence;

namespace SmartDesk.Infrastructure.Push;

// Sends what PushQueue collects (ADR-0056): each notice to every device its recipients turned
// notifications on in, if their account is active - written in the owner's language with the times in
// the office's zone, encrypted for the device (RFC 8291) and signed with the server's key (RFC 8292). A
// device its push service no longer knows (404, 410) is forgotten. Anything else that fails is logged
// and the next goes out: the inbox has the message anyway, and a reminder that waits comes too late.
public sealed class PushDispatchWorker(
    PushQueue queue,
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    IOptions<WebPushOptions> options,
    ICheckInTimes checkInTimes,
    TimeProvider timeProvider,
    ILogger<PushDispatchWorker> logger) : BackgroundService
{
    public const string HttpClientName = "WebPush";

    // Where the app's notifications show its icon (ADR-0054), from the service worker's scope.
    public const string Icon = "icons/icon-192.png";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured)
        {
            return;
        }

        using var key = VapidKeys.Load(options.Value.PublicKey, options.Value.PrivateKey);
        await foreach (var notice in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await SendAsync(notice, key, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Could not send a {Topic} notification", notice.Topic);
            }
        }
    }

    private async Task SendAsync(PushNotice notice, ECDsa key, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SmartDeskDbContext>();

        var recipients = notice.RecipientIds.Distinct().ToList();
        var devices = await db.PushSubscriptions.Where(s => recipients.Contains(s.UserId)).ToListAsync(cancellationToken);
        if (devices.Count == 0)
        {
            return;
        }

        // A switched-off account's devices stay quiet. Someone who never chose a language reads the
        // app's own, Turkish (ADR-0025).
        var owners = devices.Select(d => d.UserId).Distinct().ToList();
        var languages = await db.Users.AsNoTracking()
            .Where(u => owners.Contains(u.Id) && u.Status == AccountStatus.Active)
            .ToDictionaryAsync(u => u.Id, u => u.Language ?? Languages.Turkish, cancellationToken);
        var zone = await OfficeZoneAsync(db, cancellationToken);
        var client = httpClients.CreateClient(HttpClientName);

        var gone = new List<PushSubscription>();
        foreach (var device in devices)
        {
            if (!languages.TryGetValue(device.UserId, out var language))
            {
                continue;
            }

            var content = PushTexts.Compose(notice, Languages.CultureFor(language), zone, checkInTimes.Grace);
            if (!await DeliverAsync(client, device, content, notice.Topic, key, cancellationToken))
            {
                gone.Add(device);
            }
        }

        if (gone.Count > 0)
        {
            db.PushSubscriptions.RemoveRange(gone);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    // False when the device is gone for good, and should be forgotten.
    private async Task<bool> DeliverAsync(
        HttpClient client, PushSubscription device, PushContent content, PushTopic topic, ECDsa key, CancellationToken cancellationToken)
    {
        byte[] body;
        try
        {
            body = WebPushEncryption.Encrypt(Payload(content), PushKeys.Decode(device.P256dh)!, PushKeys.Decode(device.Auth)!);
        }
        catch (Exception exception) when (PushKeys.IsBadKey(exception))
        {
            logger.LogWarning(exception, "Device {DeviceId} has keys nothing can be encrypted for; forgetting it", device.Id);
            return false;
        }

        var endpoint = new Uri(device.Endpoint);
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new ByteArrayContent(body) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        request.Content.Headers.ContentEncoding.Add("aes128gcm");
        request.Headers.TryAddWithoutValidation("TTL", ((int)TimeToLive(topic).TotalSeconds).ToString(CultureInfo.InvariantCulture));
        request.Headers.TryAddWithoutValidation("Urgency", IsUrgent(topic) ? "high" : "normal");
        request.Headers.Authorization = VapidAuthorization.For(
            endpoint, key, options.Value.PublicKey, options.Value.Subject, timeProvider.GetUtcNow());

        using var response = await client.SendAsync(request, cancellationToken);
        if (response.IsSuccessStatusCode)
        {
            logger.LogInformation("Sent a {Topic} notification to device {DeviceId}", topic, device.Id);
            return true;
        }

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            logger.LogInformation("Device {DeviceId} has left its push service; forgetting it", device.Id);
            return false;
        }

        // Apple and the others say why in the body ("BadJwtToken").
        var reason = await response.Content.ReadAsStringAsync(cancellationToken);
        logger.LogWarning(
            "A {Topic} notification to device {DeviceId} was refused: {StatusCode} {Reason}",
            topic, device.Id, (int)response.StatusCode, reason.Length > 200 ? reason[..200] : reason);
        return true;
    }

    // What the Angular service worker shows, and opens the app on when it's tapped (ADR-0054).
    private static byte[] Payload(PushContent content) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        notification = new
        {
            title = content.Title,
            body = content.Body,
            icon = Icon,
            tag = content.Tag,
            data = new
            {
                onActionClick = new
                {
                    @default = new { operation = "navigateLastFocusedOrOpen", url = content.Url },
                },
            },
        },
    });

    // How long a push service keeps trying a device that's off: a reminder an hour late is noise.
    private static TimeSpan TimeToLive(PushTopic topic) => topic switch
    {
        PushTopic.StartingSoon or PushTopic.ReleaseWarning or PushTopic.EndingSoon => TimeSpan.FromMinutes(15),
        PushTopic.Released => TimeSpan.FromHours(1),
        PushTopic.Test => TimeSpan.FromMinutes(5),
        _ => TimeSpan.FromDays(1),
    };

    private static bool IsUrgent(PushTopic topic) =>
        topic is PushTopic.StartingSoon or PushTopic.ReleaseWarning or PushTopic.EndingSoon;

    private static async Task<TimeZoneInfo> OfficeZoneAsync(SmartDeskDbContext db, CancellationToken cancellationToken)
    {
        var id = await db.OfficeHours.AsNoTracking().Select(h => h.TimeZone).FirstOrDefaultAsync(cancellationToken);
        return id is not null && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : TimeZoneInfo.Utc;
    }
}
