using System.Buffers.Text;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Features.Push.Commands.DeletePushSubscription;
using SmartDesk.Application.Features.Push.Commands.SavePushSubscription;
using SmartDesk.Application.Features.Push.Commands.SendTestPush;
using SmartDesk.Application.Features.Push.Common;
using SmartDesk.Domain.Constants;
using SmartDesk.Domain.Entities;
using SmartDesk.Domain.Rules;
using SmartDesk.Tests.TestSupport;

namespace SmartDesk.Tests.Push;

// The devices people turn notifications on in (ADR-0056): one row each, kept in step, moved to
// whoever turns them on there last, at most a few per person, and only at a known push service.
public sealed class PushSubscriptionTests : IDisposable
{
    private static readonly DateTime Ten = new(2026, 10, 21, 10, 0, 0, DateTimeKind.Utc);
    private static readonly FakeCurrentUser Ayse = new(Guid.NewGuid(), Roles.Employee);
    private static readonly FakeCurrentUser Mehmet = new(Guid.NewGuid(), Roles.Employee);

    private readonly SqliteTestDatabase _database = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(Ten));
    private readonly CapturingPushSender _push = new();

    public void Dispose() => _database.Dispose();

    private static string Endpoint(string device = "phone") => $"https://fcm.googleapis.com/fcm/send/{device}";

    private static (string P256dh, string Auth) NewKeys()
    {
        using var key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var point = key.ExportParameters(includePrivateParameters: false).Q;
        return (Base64Url.EncodeToString([0x04, .. point.X!, .. point.Y!]), Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(16)));
    }

    private async Task SaveAsync(FakeCurrentUser user, string endpoint, (string P256dh, string Auth)? keys = null, CapturingPushSender? push = null)
    {
        var (p256dh, auth) = keys ?? NewKeys();
        await using var context = _database.CreateContext();
        await new SavePushSubscriptionCommandHandler(context, user, push ?? _push, _clock)
            .Handle(new SavePushSubscriptionCommand(endpoint, p256dh, auth), CancellationToken.None);
    }

    private async Task<List<PushSubscription>> DevicesAsync()
    {
        await using var context = _database.CreateContext();
        return await context.PushSubscriptions.AsNoTracking().OrderBy(s => s.SavedAt).ToListAsync();
    }

    [Fact]
    public async Task Turning_them_on_again_keeps_one_device_with_its_newest_keys()
    {
        await SaveAsync(Ayse, Endpoint());
        _clock.Advance(TimeSpan.FromHours(1));
        var newer = NewKeys();
        await SaveAsync(Ayse, Endpoint(), newer);

        var device = (await DevicesAsync()).Should().ContainSingle().Subject;
        device.UserId.Should().Be(Ayse.UserId!.Value);
        device.P256dh.Should().Be(newer.P256dh);
        device.Auth.Should().Be(newer.Auth);
        device.SavedAt.Should().Be(Ten.AddHours(1));
    }

    [Fact]
    public async Task A_device_is_whoever_turned_them_on_there_last()
    {
        await SaveAsync(Ayse, Endpoint("shared"));

        await SaveAsync(Mehmet, Endpoint("shared"));

        (await DevicesAsync()).Should().ContainSingle().Which.UserId.Should().Be(Mehmet.UserId!.Value);
    }

    [Fact]
    public async Task Past_the_most_devices_the_one_seen_longest_ago_is_forgotten()
    {
        for (var device = 0; device <= PushRules.MaxDevicesPerUser; device++)
        {
            await SaveAsync(Ayse, Endpoint($"device-{device}"));
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        var devices = await DevicesAsync();
        devices.Should().HaveCount(PushRules.MaxDevicesPerUser);
        devices.Should().NotContain(d => d.Endpoint == Endpoint("device-0"));
    }

    [Fact]
    public async Task Turning_them_off_forgets_the_callers_device_and_nobody_elses()
    {
        await SaveAsync(Ayse, Endpoint("ayse"));
        await SaveAsync(Mehmet, Endpoint("mehmet"));

        await using (var context = _database.CreateContext())
        {
            var handler = new DeletePushSubscriptionCommandHandler(context, Ayse);
            await handler.Handle(new DeletePushSubscriptionCommand(Endpoint("mehmet")), CancellationToken.None);
            await handler.Handle(new DeletePushSubscriptionCommand(Endpoint("ayse")), CancellationToken.None);
            await handler.Handle(new DeletePushSubscriptionCommand(Endpoint("ayse")), CancellationToken.None);
        }

        (await DevicesAsync()).Should().ContainSingle().Which.Endpoint.Should().Be(Endpoint("mehmet"));
    }

    [Fact]
    public async Task A_test_goes_to_the_callers_devices_and_says_how_many()
    {
        await SaveAsync(Ayse, Endpoint("phone"));
        await SaveAsync(Ayse, Endpoint("laptop"));

        await using var context = _database.CreateContext();
        var sent = await new SendTestPushCommandHandler(context, Ayse, _push).Handle(new SendTestPushCommand(), CancellationToken.None);
        var none = await new SendTestPushCommandHandler(context, Mehmet, _push).Handle(new SendTestPushCommand(), CancellationToken.None);

        sent.Devices.Should().Be(2);
        none.Devices.Should().Be(0);
        _push.Queued.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new PushNotice(PushTopic.Test, [Ayse.UserId!.Value]));
    }

    [Fact]
    public async Task Without_the_servers_keys_nothing_is_turned_on_or_tested()
    {
        var off = new CapturingPushSender(on: false);

        var save = () => SaveAsync(Ayse, Endpoint(), push: off);
        await save.Should().ThrowAsync<ServiceUnavailableException>();

        await using var context = _database.CreateContext();
        var test = () => new SendTestPushCommandHandler(context, Ayse, off).Handle(new SendTestPushCommand(), CancellationToken.None);
        await test.Should().ThrowAsync<ServiceUnavailableException>();
        (await DevicesAsync()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://web.push.apple.com/QGuQyavXutnMmVcWq0ZY", true)]
    [InlineData("https://fcm.googleapis.com/fcm/send/dpH5lCsTSSM:APA91bH", true)]
    [InlineData("https://wns2-par02p.notify.windows.com/w/?token=BQYAAAB", true)]
    [InlineData("https://updates.push.services.mozilla.com/wpush/v2/gAAAAAB", true)]
    [InlineData("http://fcm.googleapis.com/fcm/send/abc", false)]
    [InlineData("https://fcm.googleapis.com:8443/fcm/send/abc", false)]
    [InlineData("https://localhost/push", false)]
    [InlineData("https://169.254.169.254/latest/meta-data", false)]
    [InlineData("https://fcm.googleapis.com.example.com/send", false)]
    [InlineData("https://evilpush.apple.com.attacker.net/x", false)]
    [InlineData("not an address", false)]
    public void Only_a_known_push_services_address_is_taken(string endpoint, bool valid)
    {
        var (p256dh, auth) = NewKeys();

        var result = new SavePushSubscriptionCommandValidator().Validate(new SavePushSubscriptionCommand(endpoint, p256dh, auth));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void Only_keys_a_message_can_be_encrypted_for_are_taken()
    {
        var (p256dh, auth) = NewKeys();
        var validator = new SavePushSubscriptionCommandValidator();
        // 65 bytes that start like a point but aren't one on the curve.
        var offCurve = Base64Url.EncodeToString([0x04, .. Enumerable.Repeat((byte)1, 64)]);

        validator.Validate(new SavePushSubscriptionCommand(Endpoint(), p256dh, auth)).IsValid.Should().BeTrue();
        validator.Validate(new SavePushSubscriptionCommand(Endpoint(), offCurve, auth)).IsValid.Should().BeFalse();
        validator.Validate(new SavePushSubscriptionCommand(Endpoint(), p256dh[..40], auth)).IsValid.Should().BeFalse();
        validator.Validate(new SavePushSubscriptionCommand(Endpoint(), p256dh, auth[..10])).IsValid.Should().BeFalse();
        validator.Validate(new SavePushSubscriptionCommand(Endpoint(), "not base64url!", auth)).IsValid.Should().BeFalse();
    }
}
