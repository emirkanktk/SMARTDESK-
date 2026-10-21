using System.Buffers.Text;
using System.Security.Cryptography;
using SmartDesk.Application.Features.Push.Common;

namespace SmartDesk.Infrastructure.Push;

// The server's VAPID key pair (RFC 8292): a P-256 key, written as base64url - the public key as the
// uncompressed point browsers subscribe with, the private key as its 32-byte scalar.
public static class VapidKeys
{
    public static (string PublicKey, string PrivateKey) Generate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = key.ExportParameters(includePrivateParameters: true);
        return (Base64Url.EncodeToString([0x04, .. parameters.Q.X!, .. parameters.Q.Y!]), Base64Url.EncodeToString(parameters.D!));
    }

    // The pair as a signing key. Throws when the two aren't a P-256 pair.
    public static ECDsa Load(string publicKey, string privateKey)
    {
        var point = PushKeys.Decode(publicKey);
        var scalar = PushKeys.Decode(privateKey);
        if (point is not { Length: 65 } || point[0] != 0x04 || scalar is not { Length: 32 })
        {
            throw new CryptographicException("A VAPID key pair is a 65-byte public key and a 32-byte private key, base64url.");
        }

        var parameters = PushKeys.PublicKey(point);
        parameters.D = scalar;
        var key = ECDsa.Create(parameters);

        // Importing checks that the public key is on the curve, not that it belongs to the private
        // key: a signature made with one and checked with the other does.
        var probe = "vapid"u8.ToArray();
        using var publicOnly = ECDsa.Create(PushKeys.PublicKey(point));
        if (!publicOnly.VerifyData(probe, key.SignData(probe, HashAlgorithmName.SHA256), HashAlgorithmName.SHA256))
        {
            key.Dispose();
            throw new CryptographicException("The VAPID public key isn't the private key's.");
        }

        return key;
    }

    public static bool AreValid(string publicKey, string privateKey)
    {
        try
        {
            using var key = Load(publicKey, privateKey);
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
