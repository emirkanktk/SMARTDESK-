using System.Buffers.Text;
using System.Security.Cryptography;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Push.Common;

// The keys a browser subscribes with (RFC 8291), as base64url text: its P-256 public key, which has
// to be a point on the curve, and its 16-byte authentication secret.
public static class PushKeys
{
    public static byte[]? Decode(string? text) =>
        string.IsNullOrEmpty(text) || !Base64Url.IsValid(text) ? null : Base64Url.DecodeFromChars(text);

    public static bool IsPublicKey(string? text)
    {
        if (Decode(text) is not { Length: PushRules.PublicKeyLength } bytes || bytes[0] != 0x04)
        {
            return false;
        }

        try
        {
            using var key = ECDiffieHellman.Create(PublicKey(bytes));
            return true;
        }
        catch (Exception exception) when (IsBadKey(exception))
        {
            return false;
        }
    }

    // A point off the curve: OpenSSL says so with a CryptographicException, Windows with a
    // PlatformNotSupportedException ("not valid for this platform").
    public static bool IsBadKey(Exception exception) =>
        exception is CryptographicException or PlatformNotSupportedException;

    public static bool IsAuthSecret(string? text) => Decode(text) is { Length: PushRules.AuthSecretLength };

    // An uncompressed point - 0x04, then X and Y - as a P-256 public key.
    public static ECParameters PublicKey(byte[] uncompressedPoint) => new()
    {
        Curve = ECCurve.NamedCurves.nistP256,
        Q = new ECPoint { X = uncompressedPoint[1..33], Y = uncompressedPoint[33..65] },
    };
}
