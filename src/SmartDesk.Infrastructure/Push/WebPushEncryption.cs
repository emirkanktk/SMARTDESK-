using System.Buffers.Binary;
using System.Security.Cryptography;
using SmartDesk.Application.Features.Push.Common;

namespace SmartDesk.Infrastructure.Push;

// A push message encrypted for one device (RFC 8291), so the push service carries it without being
// able to read it. A fresh P-256 key agrees a secret with the device's key; mixed with the device's
// authentication secret and a fresh salt, it gives the AES-128-GCM key and nonce of the one record
// of an "aes128gcm" body (RFC 8188), whose header carries the salt and the fresh public key - all the
// device needs, with its own private key, to decrypt it.
public static class WebPushEncryption
{
    // The record size written in the header. A push service takes a message of 4096 bytes at least
    // (RFC 8030), so the whole body - header, delimiter and tag included - stays within that.
    public const int RecordSize = 4096;

    private const int SaltLength = 16;
    private const int TagLength = 16;
    private const int HeaderLength = SaltLength + 4 + 1 + 65;

    public const int MaxPlaintextLength = RecordSize - HeaderLength - 1 - TagLength;

    public static byte[] Encrypt(ReadOnlySpan<byte> plaintext, byte[] devicePublicKey, byte[] authSecret)
    {
        using var senderKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return Encrypt(plaintext, devicePublicKey, authSecret, senderKey, RandomNumberGenerator.GetBytes(SaltLength));
    }

    // With the sender's key and the salt given, as RFC 8291's worked example needs; never with a key
    // or salt used before.
    public static byte[] Encrypt(
        ReadOnlySpan<byte> plaintext, byte[] devicePublicKey, byte[] authSecret, ECDiffieHellman senderKey, byte[] salt)
    {
        if (plaintext.Length > MaxPlaintextLength)
        {
            throw new ArgumentException($"A push message holds at most {MaxPlaintextLength} bytes.", nameof(plaintext));
        }

        using var deviceKey = ECDiffieHellman.Create(PushKeys.PublicKey(devicePublicKey));
        var sender = senderKey.ExportParameters(includePrivateParameters: false);
        byte[] senderPublicKey = [0x04, .. sender.Q.X!, .. sender.Q.Y!];

        // IKM = HKDF(salt = auth secret, the ECDH secret, info = "WebPush: info" 0 device key sender key)
        var ecdhSecret = senderKey.DeriveRawSecretAgreement(deviceKey.PublicKey);
        byte[] keyInfo = [.. "WebPush: info\0"u8, .. devicePublicKey, .. senderPublicKey];
        var ikm = HKDF.DeriveKey(HashAlgorithmName.SHA256, ecdhSecret, 32, authSecret, keyInfo);

        // The content key and nonce, from the IKM and the salt (RFC 8188).
        var prk = HKDF.Extract(HashAlgorithmName.SHA256, ikm, salt);
        var key = HKDF.Expand(HashAlgorithmName.SHA256, prk, 16, "Content-Encoding: aes128gcm\0"u8.ToArray());
        var nonce = HKDF.Expand(HashAlgorithmName.SHA256, prk, 12, "Content-Encoding: nonce\0"u8.ToArray());

        // One record: the message and the 0x02 that ends the last record, encrypted, then the tag.
        byte[] padded = [.. plaintext, 0x02];
        var body = new byte[HeaderLength + padded.Length + TagLength];
        salt.CopyTo(body, 0);
        BinaryPrimitives.WriteUInt32BigEndian(body.AsSpan(SaltLength, 4), RecordSize);
        body[SaltLength + 4] = (byte)senderPublicKey.Length;
        senderPublicKey.CopyTo(body, SaltLength + 5);

        using var aes = new AesGcm(key, TagLength);
        aes.Encrypt(nonce, padded, body.AsSpan(HeaderLength, padded.Length), body.AsSpan(HeaderLength + padded.Length, TagLength));
        return body;
    }
}
