using System.Buffers.Text;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SmartDesk.Infrastructure.Push;

// Tells a push service which server a notification is from (VAPID, RFC 8292): a JWT for that push
// service's origin, signed with the server's private key (ES256), and the public key the device
// subscribed with. The push service takes notifications for a device only from the key it was given.
public static class VapidAuthorization
{
    // Valid this long. Apple refuses a token that lasts more than a day.
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);

    public static AuthenticationHeaderValue For(Uri endpoint, ECDsa key, string publicKey, string subject, DateTimeOffset now)
    {
        var header = Base64Url.EncodeToString("""{"typ":"JWT","alg":"ES256"}"""u8);
        var claims = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(new
        {
            aud = endpoint.GetLeftPart(UriPartial.Authority),
            exp = (now + Lifetime).ToUnixTimeSeconds(),
            sub = subject,
        }));
        var unsigned = $"{header}.{claims}";
        var signature = key.SignData(
            Encoding.ASCII.GetBytes(unsigned), HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return new AuthenticationHeaderValue("vapid", $"t={unsigned}.{Base64Url.EncodeToString(signature)}, k={publicKey}");
    }
}
