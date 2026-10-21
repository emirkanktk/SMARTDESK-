using System.Buffers.Binary;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SmartDesk.Application.Features.Push.Common;
using SmartDesk.Infrastructure.Push;

namespace SmartDesk.Tests.Push;

// The cryptography of notifications (ADR-0056): a message encrypted for one device as RFC 8291
// spells it, checked against the RFC's own worked example, and the VAPID token that tells the push
// service the message is this server's (RFC 8292).
public sealed class WebPushTests
{
    // RFC 8291, Appendix A: "When I grow up, I want to be a watermelon", encrypted with these keys
    // and this salt, is exactly this.
    private const string Plaintext = "V2hlbiBJIGdyb3cgdXAsIEkgd2FudCB0byBiZSBhIHdhdGVybWVsb24";
    private const string SenderPublicKey = "BP4z9KsN6nGRTbVYI_c7VJSPQTBtkgcy27mlmlMoZIIgDll6e3vCYLocInmYWAmS6TlzAC8wEqKK6PBru3jl7A8";
    private const string SenderPrivateKey = "yfWPiYE-n46HLnH0KqZOF1fJJU3MYrct3AELtAQ-oRw";
    private const string DevicePublicKey = "BCVxsr7N_eNgVRqvHtD0zTZsEc6-VV-JvLexhqUzORcxaOzi6-AYWXvTBHm4bjyPjs7Vd8pZGH6SRpkNtoIAiw4";
    private const string Salt = "DGv6ra1nlYgDCS1FRnbzlw";
    private const string AuthSecret = "BTBZMqHH6r4Tts7J_aSIgg";
    private const string Ciphertext = "8pfeW0KbunFT06SuDKoJH9Ql87S1QUrdirN6GcG7sFz1y1sqLgVi1VhjVkHsUoEsbI_0LpXMuGvnzQ";

    private static byte[] Bytes(string base64Url) => Base64Url.DecodeFromChars(base64Url);

    [Fact]
    public void A_message_is_encrypted_exactly_as_in_RFC_8291s_example()
    {
        var sender = PushKeys.PublicKey(Bytes(SenderPublicKey));
        sender.D = Bytes(SenderPrivateKey);
        using var senderKey = ECDiffieHellman.Create(sender);

        var body = WebPushEncryption.Encrypt(
            Bytes(Plaintext), Bytes(DevicePublicKey), Bytes(AuthSecret), senderKey, Bytes(Salt));

        // The header - the salt, a record of 4096 bytes, the sender's 65-byte key - then the record.
        byte[] header = [.. Bytes(Salt), 0x00, 0x00, 0x10, 0x00, 65, .. Bytes(SenderPublicKey)];
        body.Should().Equal([.. header, .. Bytes(Ciphertext)]);
    }

    [Fact]
    public void Every_message_has_a_salt_and_a_sender_key_of_its_own()
    {
        var device = NewDevicePublicKey();
        var auth = RandomNumberGenerator.GetBytes(16);

        var first = WebPushEncryption.Encrypt("Hello"u8, device, auth);
        var second = WebPushEncryption.Encrypt("Hello"u8, device, auth);

        first[..16].Should().NotEqual(second[..16]);
        first[21..86].Should().NotEqual(second[21..86]);
        BinaryPrimitives.ReadUInt32BigEndian(first.AsSpan(16, 4)).Should().Be(WebPushEncryption.RecordSize);
        // The header, the five bytes and their delimiter, and the tag.
        first.Should().HaveCount(86 + 5 + 1 + 16);
    }

    [Fact]
    public void A_message_too_long_for_one_record_is_refused()
    {
        var tooLong = new byte[WebPushEncryption.MaxPlaintextLength + 1];

        var encrypt = () => WebPushEncryption.Encrypt(tooLong, NewDevicePublicKey(), RandomNumberGenerator.GetBytes(16));

        encrypt.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void The_VAPID_token_is_for_the_push_services_origin_signed_with_the_servers_key()
    {
        var (publicKey, privateKey) = VapidKeys.Generate();
        using var key = VapidKeys.Load(publicKey, privateKey);
        var now = new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

        var header = VapidAuthorization.For(
            new Uri("https://web.push.apple.com/QGuQyavXutnMmVcWq0ZY"), key, publicKey, "mailto:it@example.com", now);

        header.Scheme.Should().Be("vapid");
        var parts = header.Parameter!.Split(", ");
        parts[1].Should().Be($"k={publicKey}");
        var token = parts[0]["t=".Length..].Split('.');
        using (var tokenHeader = JsonDocument.Parse(Bytes(token[0])))
        {
            tokenHeader.RootElement.GetProperty("alg").GetString().Should().Be("ES256");
        }

        using var claims = JsonDocument.Parse(Bytes(token[1]));
        claims.RootElement.GetProperty("aud").GetString().Should().Be("https://web.push.apple.com");
        claims.RootElement.GetProperty("exp").GetInt64().Should().Be(now.AddHours(12).ToUnixTimeSeconds());
        claims.RootElement.GetProperty("sub").GetString().Should().Be("mailto:it@example.com");

        using var verifier = ECDsa.Create(PushKeys.PublicKey(Bytes(publicKey)));
        verifier.VerifyData(
                Encoding.ASCII.GetBytes($"{token[0]}.{token[1]}"),
                Bytes(token[2]),
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
            .Should().BeTrue();
    }

    [Fact]
    public void The_VAPID_keys_have_to_be_a_pair()
    {
        var (publicKey, privateKey) = VapidKeys.Generate();
        var (otherPublicKey, _) = VapidKeys.Generate();

        VapidKeys.AreValid(publicKey, privateKey).Should().BeTrue();
        VapidKeys.AreValid(otherPublicKey, privateKey).Should().BeFalse();
        VapidKeys.AreValid(publicKey, "c2hvcnQ").Should().BeFalse();
        VapidKeys.AreValid(string.Empty, privateKey).Should().BeFalse();
    }

    [Theory]
    [InlineData("mailto:it@example.com", true)]
    [InlineData("https://smartdesk.example.com/contact", true)]
    [InlineData("http://smartdesk.example.com", false)]
    [InlineData("mailto:admin@localhost", false)]
    [InlineData("mailto:admin@smartdesk.local", false)]
    [InlineData("it@example.com", false)]
    public void The_contact_is_a_mailto_or_https_address_Apple_takes(string subject, bool valid) =>
        new WebPushOptions { Subject = subject }.HasValidSubject.Should().Be(valid);

    private static byte[] NewDevicePublicKey()
    {
        using var device = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        var point = device.ExportParameters(includePrivateParameters: false).Q;
        return [0x04, .. point.X!, .. point.Y!];
    }
}
