using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Application.Features.Push.Common;
using SmartDesk.Infrastructure.Identity;
using SmartDesk.Infrastructure.Persistence;
using SmartDesk.Infrastructure.Persistence.Seed;
using SmartDesk.Infrastructure.Push;

namespace SmartDesk.IntegrationTests;

// Notifications on phones and browsers over HTTP, on PostgreSQL (ADR-0056). A device made up here does
// what a browser does: subscribes with its own keys, and reads what reaches its push service - which
// here is a stand-in - by decrypting it as RFC 8291 says, with its private key.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PushTests(PostgresApiFixture fixture)
{
    private static DateTime Start(int hour) => DateTime.UtcNow.Date.AddDays(131).AddHours(hour);

    [Fact]
    public async Task Without_the_servers_keys_notifications_are_off()
    {
        var ayse = await ClientAsync(fixture, "employee1@smartdesk.local");
        using var device = new Device();

        var status = (await ayse.GetFromJsonAsync<Status>("/api/push"))!;
        status.Should().Be(new Status(false, null));
        (await ayse.PutAsJsonAsync("/api/push/subscription", device.Subscription)).StatusCode
            .Should().Be(HttpStatusCode.ServiceUnavailable);
        (await ayse.PostAsync("/api/push/test", null)).StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task Only_a_known_push_services_address_is_taken()
    {
        var ayse = await ClientAsync(fixture, "employee1@smartdesk.local");
        using var device = new Device("https://localhost:5291/api/admin");

        var refused = await ayse.PutAsJsonAsync("/api/push/subscription", device.Subscription);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync());
        problem.RootElement.GetProperty("errors").TryGetProperty("Endpoint", out _).Should().BeTrue();
    }

    [Fact]
    public async Task A_device_gets_a_test_and_a_meeting_invitation_in_its_owners_language_and_can_read_them()
    {
        // Not disposed: the host it serves runs until the collection ends.
        var pushService = new PushService();
        var (host, publicKey) = PushHost(pushService);
        var ayse = await ClientAsync(host, "employee1@smartdesk.local", "tr");
        var manager = await ClientAsync(host, "manager@smartdesk.local");
        var admin = await ClientAsync(host, "admin@smartdesk.local");
        using var device = new Device();

        (await ayse.GetFromJsonAsync<Status>("/api/push"))!.Should().Be(new Status(true, publicKey));
        (await ayse.PutAsJsonAsync("/api/push/subscription", device.Subscription)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        var test = await ayse.PostAsync("/api/push/test", null);
        test.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await test.Content.ReadFromJsonAsync<TestSent>())!.Devices.Should().BeGreaterThanOrEqualTo(1);

        var sent = await pushService.NextAsync(device, n => n.Tag == "test");
        sent.Authorization!.Scheme.Should().Be("vapid");
        sent.Authorization.Parameter.Should().EndWith($", k={publicKey}");
        VapidTokenFor(sent.Authorization.Parameter!, publicKey).GetProperty("aud").GetString()
            .Should().Be("https://fcm.googleapis.com");
        sent.ContentEncoding.Should().Be("aes128gcm");
        sent.TimeToLive.Should().Be("300");
        sent.Notification.Title.Should().Be("Bildirimler açık");
        sent.Notification.Url.Should().Be("/profile");

        // The manager invites Ayşe: the inbox message reaches her phone, in Turkish.
        var ayseId = (await ayse.GetFromJsonAsync<Me>("/api/auth/me"))!.Id;
        var room = await AddRoomAsync(admin);
        var booked = await manager.PostAsJsonAsync("/api/reservations", new
        {
            resourceId = room,
            startDateTime = Start(10),
            endDateTime = Start(11),
            attendeeIds = new[] { ayseId },
        });
        booked.StatusCode.Should().Be(HttpStatusCode.Created);

        var invitation = await pushService.NextAsync(device, n => n.Tag.StartsWith("inbox-", StringComparison.Ordinal));
        invitation.Notification.Title.Should().EndWith("sizi bir toplantıya davet etti");
        invitation.Notification.Body.Should().StartWith("Meeting room ");
        invitation.Notification.Url.Should().Be("/inbox");
        invitation.TimeToLive.Should().Be("86400");

        (await ayse.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/api/push/subscription")
        {
            Content = JsonContent.Create(new { endpoint = device.Endpoint }),
        })).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task A_device_its_push_service_has_let_go_is_forgotten()
    {
        var pushService = new PushService { Answer = HttpStatusCode.Gone };
        var (host, _) = PushHost(pushService);
        var mehmet = await ClientAsync(host, "employee2@smartdesk.local");
        using var device = new Device();
        (await mehmet.PutAsJsonAsync("/api/push/subscription", device.Subscription)).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        (await mehmet.PostAsync("/api/push/test", null)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await pushService.NextAsync(device, n => n.Tag == "test");

        // Forgotten once the push service has answered 410.
        await AssertEventuallyAsync(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SmartDeskDbContext>();
            (await db.PushSubscriptions.AnyAsync(s => s.Endpoint == device.Endpoint)).Should().BeFalse();
        });
    }

    // A host with notifications on: VAPID keys of its own, and the stand-in push service.
    private (WebApplicationFactory<Program> Host, string PublicKey) PushHost(PushService pushService)
    {
        var (publicKey, privateKey) = VapidKeys.Generate();
        var host = fixture.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WebPush:PublicKey", publicKey);
            builder.UseSetting("WebPush:PrivateKey", privateKey);
            builder.UseSetting("WebPush:Subject", "mailto:it@example.com");
            builder.ConfigureTestServices(services => services
                .AddHttpClient(PushDispatchWorker.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => pushService)
                .SetHandlerLifetime(Timeout.InfiniteTimeSpan));
        });
        return (host, publicKey);
    }

    private static JsonElement VapidTokenFor(string parameter, string publicKey)
    {
        var token = parameter.Split(", ")[0]["t=".Length..].Split('.');
        using var key = ECDsa.Create(PushKeys.PublicKey(Base64Url.DecodeFromChars(publicKey)));
        key.VerifyData(
                Encoding.ASCII.GetBytes($"{token[0]}.{token[1]}"),
                Base64Url.DecodeFromChars(token[2]),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            .Should().BeTrue();
        return JsonDocument.Parse(Base64Url.DecodeFromChars(token[1])).RootElement.Clone();
    }

    private static async Task AssertEventuallyAsync(Func<Task> assertion)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await assertion();
                return;
            }
            catch when (attempt < 50)
            {
                await Task.Delay(100);
            }
        }
    }

    private static async Task<Guid> AddRoomAsync(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/resources", new
        {
            name = $"Meeting room {Guid.NewGuid():N}"[..21],
            resourceTypeId = ResourceTypeSeedData.MeetingRoomId,
            capacity = 6,
            location = "3rd Floor",
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Room>())!.Id;
    }

    // Signed in in a language, which the account then keeps (ADR-0023): its notifications are in it.
    private static async Task<HttpClient> ClientAsync(WebApplicationFactory<Program> host, string email, string? language = null)
    {
        using var login = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { email, password = IdentityDataSeeder.DemoPassword }),
        };
        if (language is not null)
        {
            login.Headers.Add("Accept-Language", language);
        }

        var response = await host.CreateClient().SendAsync(login);
        response.EnsureSuccessStatusCode();
        var body = (await response.Content.ReadFromJsonAsync<LoginBody>())!;
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.AccessToken);
        return client;
    }

    // A browser's side of Web Push: its own P-256 key and authentication secret, and the means to read
    // what's encrypted for it.
    private sealed class Device(string? endpoint = null) : IDisposable
    {
        private readonly ECDiffieHellman _key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        private readonly byte[] _auth = RandomNumberGenerator.GetBytes(16);

        public string Endpoint { get; } = endpoint ?? $"https://fcm.googleapis.com/fcm/send/{Guid.NewGuid():N}";

        private byte[] PublicKey
        {
            get
            {
                var point = _key.ExportParameters(includePrivateParameters: false).Q;
                return [0x04, .. point.X!, .. point.Y!];
            }
        }

        public object Subscription => new
        {
            endpoint = Endpoint,
            p256dh = Base64Url.EncodeToString(PublicKey),
            auth = Base64Url.EncodeToString(_auth),
        };

        // RFC 8291 from the device's end: the header's salt and sender key, the same derivation
        // with the device's private key, one record, and the 0x02 that ends it.
        public byte[] Decrypt(byte[] body)
        {
            var salt = body[..16];
            var senderPublicKey = body[21..(21 + body[20])];
            var record = body[(21 + body[20])..];

            using var sender = ECDiffieHellman.Create(PushKeys.PublicKey(senderPublicKey));
            var ecdhSecret = _key.DeriveRawSecretAgreement(sender.PublicKey);
            var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdhSecret, 32, _auth, [.. "WebPush: info\0"u8, .. PublicKey, .. senderPublicKey]);
            var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
            var key = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
            var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, "Content-Encoding: nonce\0"u8.ToArray());

            var padded = new byte[record.Length - 16];
            using var aes = new AesGcm(key, 16);
            aes.Decrypt(nonce, record[..^16], record[^16..], padded);
            padded[^1].Should().Be(0x02);
            return padded[..^1];
        }

        public void Dispose() => _key.Dispose();
    }

    // The stand-in for Apple's, Google's or Microsoft's push service: keeps what it's sent, as it
    // arrives, and answers as told.
    private sealed class PushService : HttpMessageHandler
    {
        private readonly Channel<Received> _received = Channel.CreateUnbounded<Received>();

        public HttpStatusCode Answer { get; init; } = HttpStatusCode.Created;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await _received.Writer.WriteAsync(
                new Received(
                    request.RequestUri!.ToString(),
                    request.Headers.Authorization is { } authorization
                        ? new AuthenticationHeaderValue(authorization.Scheme, authorization.Parameter)
                        : null,
                    string.Join(",", request.Content!.Headers.ContentEncoding),
                    request.Headers.GetValues("TTL").Single(),
                    await request.Content.ReadAsByteArrayAsync(cancellationToken)),
                cancellationToken);
            return new HttpResponseMessage(Answer);
        }

        // The next notification to this device that matches, as the device reads it.
        public async Task<Sent> NextAsync(Device device, Func<Notification, bool> matches)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (true)
            {
                var received = await _received.Reader.ReadAsync(timeout.Token);
                if (received.Endpoint != device.Endpoint)
                {
                    continue;
                }

                using var payload = JsonDocument.Parse(device.Decrypt(received.Body));
                var shown = payload.RootElement.GetProperty("notification");
                var notification = new Notification(
                    shown.GetProperty("title").GetString()!,
                    shown.GetProperty("body").GetString()!,
                    shown.GetProperty("tag").GetString()!,
                    shown.GetProperty("data").GetProperty("onActionClick").GetProperty("default").GetProperty("url").GetString()!);
                if (matches(notification))
                {
                    return new Sent(received.Authorization, received.ContentEncoding, received.TimeToLive, notification);
                }
            }
        }

        private sealed record Received(
            string Endpoint, AuthenticationHeaderValue? Authorization, string ContentEncoding, string TimeToLive, byte[] Body);
    }

    private sealed record Sent(AuthenticationHeaderValue? Authorization, string ContentEncoding, string TimeToLive, Notification Notification);

    private sealed record Notification(string Title, string Body, string Tag, string Url);

    private sealed record Status(bool Enabled, string? PublicKey);

    private sealed record TestSent(int Devices);

    private sealed record LoginBody(string AccessToken);

    private sealed record Me(Guid Id);

    private sealed record Room(Guid Id);
}
