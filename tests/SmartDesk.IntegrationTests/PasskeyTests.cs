using System.Buffers.Binary;
using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Infrastructure.Identity;

namespace SmartDesk.IntegrationTests;

// Passkeys over HTTP, on PostgreSQL (ADR-0057). An authenticator made up here does what Face ID or
// Windows Hello does behind the browser: makes a key for the site, and signs the server's challenges
// with it. Identity's own WebAuthn checks run on what it sends, as they do on what a phone sends.
[Collection(PostgresApiCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PasskeyTests(PostgresApiFixture fixture)
{
    // Where the app is served in Development, so where its passkeys may be made and used.
    private const string App = "http://localhost:4200";
    private const string Password = "Passkey-Owner-1";

    [Fact]
    public async Task A_passkey_added_with_the_password_signs_in_without_one()
    {
        var owner = await NewAccountAsync();
        using var authenticator = new Authenticator();

        var options = await StartAsync(owner.Client, "/api/auth/passkeys/creation-options", new { password = Password });
        options.Options.GetProperty("rp").GetProperty("id").GetString().Should().Be("localhost");
        options.Options.GetProperty("user").GetProperty("name").GetString().Should().Be(owner.Email);
        var selection = options.Options.GetProperty("authenticatorSelection");
        selection.GetProperty("residentKey").GetString().Should().Be("required");
        selection.GetProperty("userVerification").GetString().Should().Be("required");

        var added = await PostAsync(owner.Client, "/api/auth/passkeys", new
        {
            credential = authenticator.Create(options.Options, App),
            token = options.Token,
            name = "  Test phone ",
        });

        added.StatusCode.Should().Be(HttpStatusCode.Created);
        var passkey = (await added.Content.ReadFromJsonAsync<Passkey>())!;
        passkey.Name.Should().Be("Test phone");
        passkey.Synced.Should().BeTrue();
        (await owner.Client.GetFromJsonAsync<List<Passkey>>("/api/auth/passkeys")).Should().ContainSingle()
            .Which.Id.Should().Be(passkey.Id);

        var guest = fixture.CreateClient(new() { HandleCookies = false });
        var request = await StartAsync(guest, "/api/auth/passkeys/request-options", new { });
        request.Options.GetProperty("rpId").GetString().Should().Be("localhost");
        request.Options.GetProperty("allowCredentials").GetArrayLength().Should().Be(0, "nobody is known yet");
        var answer = new { credential = authenticator.Get(request.Options, App), token = request.Token };
        var signedIn = await PostAsync(guest, "/api/auth/passkeys/sign-in", answer);

        signedIn.StatusCode.Should().Be(HttpStatusCode.OK);
        signedIn.Headers.GetValues("Set-Cookie").Should().Contain(c => c.StartsWith("smartdesk_session=", StringComparison.Ordinal));
        var token = (await signedIn.Content.ReadFromJsonAsync<LoginBody>())!.AccessToken;
        (await Bearer(fixture, token).GetFromJsonAsync<Me>("/api/auth/me"))!.Email.Should().Be(owner.Email);

        var replayed = await PostAsync(guest, "/api/auth/passkeys/sign-in", answer);
        replayed.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a challenge is good for one answer");
        (await DetailAsync(replayed)).Should().Contain("expired");
    }

    [Fact]
    public async Task Adding_one_takes_the_password_and_only_the_apps_own_address_may_ask()
    {
        var owner = await NewAccountAsync();

        var wrong = await PostAsync(owner.Client, "/api/auth/passkeys/creation-options", new { password = "Not-The-One-1" });
        wrong.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ErrorsAsync(wrong)).Should().Contain("Password");

        var elsewhere = await PostAsync(owner.Client, "/api/auth/passkeys/creation-options", new { password = Password }, "https://smartdesk.example.net");
        elsewhere.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var nowhere = await PostAsync(owner.Client, "/api/auth/passkeys/creation-options", new { password = Password }, origin: null);
        nowhere.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var guest = fixture.CreateClient();
        (await PostAsync(guest, "/api/auth/passkeys/request-options", new { }, "https://smartdesk.example.net"))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_answer_from_another_page_or_for_another_ceremony_or_account_is_refused()
    {
        var owner = await NewAccountAsync();
        var other = await NewAccountAsync();
        using var authenticator = new Authenticator();
        await AddPasskeyAsync(owner.Client, authenticator);
        var guest = fixture.CreateClient();

        // A look-alike site relaying SmartDesk's challenge: what the passkey signs names the page
        // it was really used on.
        var relayed = await StartAsync(guest, "/api/auth/passkeys/request-options", new { });
        var phished = await PostAsync(guest, "/api/auth/passkeys/sign-in", new
        {
            credential = authenticator.Get(relayed.Options, "https://smartdesk-login.example.net"),
            token = relayed.Token,
        });
        phished.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await DetailAsync(phished)).Should().Contain("couldn't be checked");

        // A token for adding one isn't one for signing in.
        var adding = await StartAsync(owner.Client, "/api/auth/passkeys/creation-options", new { password = Password });
        var mixed = await PostAsync(guest, "/api/auth/passkeys/sign-in", new
        {
            credential = authenticator.Get(adding.Options.GetProperty("rp").GetProperty("id").GetString()!, adding.Options, App),
            token = adding.Token,
        });
        mixed.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // Nor is one person's token for adding one good for anybody else.
        var theirs = await StartAsync(owner.Client, "/api/auth/passkeys/creation-options", new { password = Password });
        using var second = new Authenticator();
        var taken = await PostAsync(other.Client, "/api/auth/passkeys", new
        {
            credential = second.Create(theirs.Options, App),
            token = theirs.Token,
            name = "Not mine",
        });
        taken.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await other.Client.GetFromJsonAsync<List<Passkey>>("/api/auth/passkeys")).Should().BeEmpty();
        (await owner.Client.GetFromJsonAsync<List<Passkey>>("/api/auth/passkeys")).Should().ContainSingle();
    }

    [Fact]
    public async Task A_removed_passkey_signs_nobody_in_and_the_history_keeps_both()
    {
        var owner = await NewAccountAsync();
        using var authenticator = new Authenticator();
        var passkey = await AddPasskeyAsync(owner.Client, authenticator);

        (await owner.Client.DeleteAsync($"/api/auth/passkeys/{passkey.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await owner.Client.GetAsync("/api/auth/me")).StatusCode.Should().Be(HttpStatusCode.OK, "the session goes on");
        (await owner.Client.GetFromJsonAsync<List<Passkey>>("/api/auth/passkeys")).Should().BeEmpty();
        (await owner.Client.DeleteAsync($"/api/auth/passkeys/{passkey.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var guest = fixture.CreateClient();
        var request = await StartAsync(guest, "/api/auth/passkeys/request-options", new { });
        var refused = await PostAsync(guest, "/api/auth/passkeys/sign-in", new
        {
            credential = authenticator.Get(request.Options, App),
            token = request.Token,
        });
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await DetailAsync(refused)).Should().Contain("isn't on a SmartDesk account");

        var admin = await SignInAsync(fixture, "admin@smartdesk.local", IdentityDataSeeder.DemoPassword);
        var history = (await admin.GetFromJsonAsync<AuditPage>($"/api/audit?entityType=Passkey&entityId={passkey.Id}"))!;
        history.Items.Select(e => (e.Action, e.Label)).Should().Equal(("Deleted", "Test phone"), ("Created", "Test phone"));
        history.Items.Should().OnlyContain(e => e.UserId == owner.Id);
        history.People[owner.Id].Should().Be("Pass Key");
    }

    [Fact]
    public async Task A_disabled_account_cant_sign_in_with_its_passkey()
    {
        var owner = await NewAccountAsync();
        using var authenticator = new Authenticator();
        await AddPasskeyAsync(owner.Client, authenticator);
        var admin = await SignInAsync(fixture, "admin@smartdesk.local", IdentityDataSeeder.DemoPassword);
        (await admin.PostAsync($"/api/users/{owner.Id}/disable", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var guest = fixture.CreateClient();
        var request = await StartAsync(guest, "/api/auth/passkeys/request-options", new { });
        var refused = await PostAsync(guest, "/api/auth/passkeys/sign-in", new
        {
            credential = authenticator.Get(request.Options, App),
            token = request.Token,
        });

        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_challenge_answered_too_late_is_refused()
    {
        var owner = await NewAccountAsync();
        using var authenticator = new Authenticator();
        await AddPasskeyAsync(owner.Client, authenticator);

        // Only the passkeys' clock moves: the rest of the app keeps the real one.
        var clock = new MovableClock();
        var host = fixture.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<IPasskeys>(sp => ActivatorUtilities.CreateInstance<Passkeys>(sp, (TimeProvider)clock))));
        var guest = host.CreateClient();
        var request = await StartAsync(guest, "/api/auth/passkeys/request-options", new { });
        clock.Ahead = TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1);

        var late = await PostAsync(guest, "/api/auth/passkeys/sign-in", new
        {
            credential = authenticator.Get(request.Options, App),
            token = request.Token,
        });

        late.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await DetailAsync(late)).Should().Contain("expired");
    }

    private async Task<Passkey> AddPasskeyAsync(HttpClient owner, Authenticator authenticator)
    {
        var options = await StartAsync(owner, "/api/auth/passkeys/creation-options", new { password = Password });
        var added = await PostAsync(owner, "/api/auth/passkeys", new
        {
            credential = authenticator.Create(options.Options, App),
            token = options.Token,
            name = "Test phone",
        });
        added.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await added.Content.ReadFromJsonAsync<Passkey>())!;
    }

    // An account of the test's own, signed in with the password its owner chose: a passkey test
    // locks nobody else out with a wrong password.
    private async Task<Account> NewAccountAsync()
    {
        const string temporary = "Temp-Pass-2026";
        var admin = await SignInAsync(fixture, "admin@smartdesk.local", IdentityDataSeeder.DemoPassword);
        var email = $"passkey-{Guid.NewGuid():N}@integration.example";
        (await admin.PostAsJsonAsync("/api/users", new { firstName = "Pass", lastName = "Key", email, role = "Employee", password = temporary }))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var first = await SignInAsync(fixture, email, temporary);
        (await first.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = temporary, newPassword = Password }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var client = await SignInAsync(fixture, email, Password);
        return new Account(client, email, (await client.GetFromJsonAsync<Me>("/api/auth/me"))!.Id);
    }

    private static async Task<HttpClient> SignInAsync(WebApplicationFactory<Program> host, string email, string password)
    {
        var response = await host.CreateClient().PostAsJsonAsync("/api/auth/login", new { email, password });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return Bearer(host, (await response.Content.ReadFromJsonAsync<LoginBody>())!.AccessToken);
    }

    private static HttpClient Bearer(WebApplicationFactory<Program> host, string token)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    // As a browser posts: from the page's own address, in its Origin header.
    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string url, object body, string? origin = App)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (origin is not null)
        {
            request.Headers.Add("Origin", origin);
        }

        return await client.SendAsync(request);
    }

    private static async Task<Ceremony> StartAsync(HttpClient client, string url, object body)
    {
        var response = await PostAsync(client, url, body);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<Ceremony>())!;
    }

    private static async Task<string> DetailAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("detail").GetString()!;
    }

    private static async Task<IReadOnlyList<string>> ErrorsAsync(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return problem.RootElement.GetProperty("errors").EnumerateObject().Select(e => e.Name).ToList();
    }

    // What a phone's or a laptop's authenticator does behind navigator.credentials (WebAuthn Level
    // 3), answering in the JSON PublicKeyCredential.toJSON() writes: a P-256 key for each passkey,
    // kept for its site with the account it was made for. It verifies the person every time, as Face
    // ID does, and its passkeys are synced, as iCloud Keychain's are.
    private sealed class Authenticator : IDisposable
    {
        // User present and verified, backup eligible and backed up; then, when making a passkey,
        // that its key follows.
        private const byte Flags = 0x01 | 0x04 | 0x08 | 0x10;
        private const byte AttestedCredentialData = 0x40;

        private readonly List<Credential> _credentials = [];
        private uint _counter;

        public JsonObject Create(JsonElement options, string origin)
        {
            var rpId = options.GetProperty("rp").GetProperty("id").GetString()!;
            options.GetProperty("pubKeyCredParams").EnumerateArray().Select(p => p.GetProperty("alg").GetInt32())
                .Should().Contain(-7, "every authenticator can sign with ES256");
            var credential = new Credential(
                RandomNumberGenerator.GetBytes(16),
                ECDsa.Create(ECCurve.NamedCurves.nistP256),
                rpId,
                Base64Url.DecodeFromChars(options.GetProperty("user").GetProperty("id").GetString()));
            _credentials.Add(credential);

            var point = credential.Key.ExportParameters(includePrivateParameters: false).Q;
            var publicKey = Cbor.Encode(new CborMap((1, 2), (3, -7), (-1, 1), (-2, point.X!), (-3, point.Y!)));
            var authenticatorData = Concat(
                SHA256.HashData(Encoding.UTF8.GetBytes(rpId)),
                [Flags | AttestedCredentialData],
                Counter(),
                // No AAGUID: a password manager's passkey says nothing of its maker either.
                new byte[16],
                [(byte)(credential.Id.Length >> 8), (byte)credential.Id.Length],
                credential.Id,
                publicKey);
            var attestation = Cbor.Encode(new CborMap(("fmt", "none"), ("attStmt", new CborMap()), ("authData", authenticatorData)));

            return Answer(credential, new JsonObject
            {
                ["clientDataJSON"] = Base64Url.EncodeToString(ClientData("webauthn.create", options, origin)),
                ["attestationObject"] = Base64Url.EncodeToString(attestation),
                ["authenticatorData"] = Base64Url.EncodeToString(authenticatorData),
                ["publicKey"] = Base64Url.EncodeToString(credential.Key.ExportSubjectPublicKeyInfo()),
                ["publicKeyAlgorithm"] = -7,
                ["transports"] = new JsonArray("internal", "hybrid"),
            });
        }

        public JsonObject Get(JsonElement options, string origin) =>
            Get(options.GetProperty("rpId").GetString()!, options, origin);

        // The passkey kept for this site, signing whatever challenge it's shown.
        public JsonObject Get(string rpId, JsonElement options, string origin)
        {
            var credential = _credentials.Last(c => c.RpId == rpId);
            var clientData = ClientData("webauthn.get", options, origin);
            var authenticatorData = Concat(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)), [Flags], Counter());
            var signature = credential.Key.SignData(
                Concat(authenticatorData, SHA256.HashData(clientData)), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

            return Answer(credential, new JsonObject
            {
                ["clientDataJSON"] = Base64Url.EncodeToString(clientData),
                ["authenticatorData"] = Base64Url.EncodeToString(authenticatorData),
                ["signature"] = Base64Url.EncodeToString(signature),
                ["userHandle"] = Base64Url.EncodeToString(credential.UserHandle),
            });
        }

        public void Dispose()
        {
            foreach (var credential in _credentials)
            {
                credential.Key.Dispose();
            }
        }

        private byte[] Counter()
        {
            var counter = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(counter, ++_counter);
            return counter;
        }

        // What the browser writes for the passkey to sign: the ceremony, the server's challenge as
        // it came, and the page's address.
        private static byte[] ClientData(string type, JsonElement options, string origin) =>
            JsonSerializer.SerializeToUtf8Bytes(new
            {
                type,
                challenge = options.GetProperty("challenge").GetString(),
                origin,
                crossOrigin = false,
            });

        private static JsonObject Answer(Credential credential, JsonObject response) => new()
        {
            ["id"] = Base64Url.EncodeToString(credential.Id),
            ["rawId"] = Base64Url.EncodeToString(credential.Id),
            ["type"] = "public-key",
            ["authenticatorAttachment"] = "platform",
            ["clientExtensionResults"] = new JsonObject(),
            ["response"] = response,
        };

        private static byte[] Concat(params byte[][] parts) => parts.SelectMany(part => part).ToArray();

        private sealed record Credential(byte[] Id, ECDsa Key, string RpId, byte[] UserHandle);
    }

    // Just enough CBOR (RFC 8949) for an attestation object and a COSE key: small integers, byte and
    // text strings, and maps.
    private sealed record CborMap(params (object Key, object Value)[] Entries);

    private static class Cbor
    {
        public static byte[] Encode(object value)
        {
            var output = new List<byte>();
            Write(output, value);
            return [.. output];
        }

        private static void Write(List<byte> output, object value)
        {
            switch (value)
            {
                case int number when number >= 0:
                    Head(output, 0, (ulong)number);
                    break;
                case int number:
                    Head(output, 1, (ulong)(-1L - number));
                    break;
                case byte[] bytes:
                    Head(output, 2, (ulong)bytes.Length);
                    output.AddRange(bytes);
                    break;
                case string text:
                    var utf8 = Encoding.UTF8.GetBytes(text);
                    Head(output, 3, (ulong)utf8.Length);
                    output.AddRange(utf8);
                    break;
                case CborMap map:
                    Head(output, 5, (ulong)map.Entries.Length);
                    foreach (var (key, item) in map.Entries)
                    {
                        Write(output, key);
                        Write(output, item);
                    }

                    break;
                default:
                    throw new ArgumentException($"No CBOR here for {value.GetType()}.");
            }
        }

        private static void Head(List<byte> output, int major, ulong length)
        {
            var type = (byte)(major << 5);
            if (length < 24)
            {
                output.Add((byte)(type | (byte)length));
            }
            else if (length <= byte.MaxValue)
            {
                output.AddRange([(byte)(type | 24), (byte)length]);
            }
            else
            {
                output.AddRange([(byte)(type | 25), (byte)(length >> 8), (byte)length]);
            }
        }
    }

    private sealed class MovableClock : TimeProvider
    {
        public TimeSpan Ahead { get; set; }

        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + Ahead;
    }

    private sealed record Account(HttpClient Client, string Email, Guid Id);

    private sealed record Ceremony(JsonElement Options, string Token);

    private sealed record Passkey(string Id, string Name, DateTimeOffset CreatedAt, bool Synced);

    private sealed record LoginBody(string AccessToken);

    private sealed record Me(Guid Id, string Email);

    private sealed record AuditEntry(string Action, Guid? UserId, string? Label);

    private sealed record AuditPage(List<AuditEntry> Items, Dictionary<Guid, string> People);
}
