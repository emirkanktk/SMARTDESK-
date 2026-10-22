using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Common.Models;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Infrastructure.Identity;

// Passkeys checked by ASP.NET Core Identity's own WebAuthn handler (ADR-0057). Identity's
// SignInManager keeps a ceremony's state in a cookie and signs in with one; this API has neither,
// so the state goes to the browser and back inside a protected token, and the handler is made for
// each request with the domain of the page's address.
public sealed class Passkeys(
    UserManager<ApplicationUser> userManager,
    IIdentityService identityService,
    IHttpContextAccessor httpContextAccessor,
    IDataProtectionProvider dataProtection,
    PasskeyOrigins origins,
    PasskeyChallengeLog challengeLog,
    TimeProvider timeProvider,
    ILogger<Passkeys> logger) : IPasskeys
{
    private const string Creating = "create";
    private const string Requesting = "get";

    private readonly IDataProtector _protector = dataProtection.CreateProtector("SmartDesk.Passkeys.Challenge");

    public async Task<PasskeyChallenge> CreationOptionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (context, origin) = Request();
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException();
        var email = user.Email ?? string.Empty;
        var name = $"{user.FirstName} {user.LastName}".Trim();

        // The device shows the email and name when it offers the passkey.
        var options = await HandlerFor(origin).MakeCreationOptionsAsync(
            new PasskeyUserEntity
            {
                Id = await userManager.GetUserIdAsync(user),
                Name = email,
                DisplayName = name.Length > 0 ? name : email,
            },
            context);
        return new PasskeyChallenge(options.CreationOptionsJson, Protect(Creating, userId, origin, options.AttestationState));
    }

    public async Task<PasskeyAdded> AddAsync(
        Guid userId, string credentialJson, string token, string name, CancellationToken cancellationToken)
    {
        var (context, origin) = Request();
        if (Open(token, Creating, origin) is not { } challenge || challenge.UserId != userId)
        {
            return new PasskeyAdded(PasskeyCheck.Expired, null);
        }

        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException();
        var result = await HandlerFor(origin).PerformAttestationAsync(new PasskeyAttestationContext
        {
            HttpContext = context,
            CredentialJson = credentialJson,
            AttestationState = challenge.State,
        });
        if (!result.Succeeded || result.UserEntity?.Id != await userManager.GetUserIdAsync(user))
        {
            logger.LogInformation("A new passkey for {UserId} didn't check out: {Reason}", userId, result.Failure?.Message);
            return new PasskeyAdded(PasskeyCheck.Failed, null);
        }

        var passkey = result.Passkey!;
        passkey.Name = name;
        Ensure(await userManager.AddOrUpdatePasskeyAsync(user, passkey), "add a passkey");
        return new PasskeyAdded(PasskeyCheck.Passed, ToDto(passkey));
    }

    public async Task<IReadOnlyList<PasskeyDto>> ListAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException();
        var passkeys = await userManager.GetPasskeysAsync(user);
        return passkeys.OrderByDescending(p => p.CreatedAt).Select(ToDto).ToList();
    }

    public async Task<bool> RemoveAsync(Guid userId, string passkeyId, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(userId.ToString()) ?? throw new UnauthorizedException();
        if (FromBase64Url(passkeyId) is not { } credentialId || await userManager.GetPasskeyAsync(user, credentialId) is null)
        {
            return false;
        }

        Ensure(await userManager.RemovePasskeyAsync(user, credentialId), "remove a passkey");
        return true;
    }

    public async Task<PasskeyChallenge> RequestOptionsAsync(CancellationToken cancellationToken)
    {
        var (context, origin) = Request();
        var options = await HandlerFor(origin).MakeRequestOptionsAsync(null, context);
        return new PasskeyChallenge(options.RequestOptionsJson, Protect(Requesting, null, origin, options.AssertionState));
    }

    public async Task<PasskeySignIn> SignInAsync(string credentialJson, string token, CancellationToken cancellationToken)
    {
        var (context, origin) = Request();
        if (Open(token, Requesting, origin) is not { } challenge)
        {
            return new PasskeySignIn(PasskeyCheck.Expired, null);
        }

        // A passkey taken off its account is still on the device, which goes on offering it: that
        // gets an answer of its own, saying what to do about it.
        if (CredentialIdOf(credentialJson) is not { } credentialId)
        {
            return new PasskeySignIn(PasskeyCheck.Failed, null);
        }

        if (await userManager.FindByPasskeyIdAsync(credentialId) is null)
        {
            return new PasskeySignIn(PasskeyCheck.Unknown, null);
        }

        var result = await HandlerFor(origin).PerformAssertionAsync(new PasskeyAssertionContext
        {
            HttpContext = context,
            CredentialJson = credentialJson,
            AssertionState = challenge.State,
        });
        if (!result.Succeeded)
        {
            logger.LogInformation("A passkey sign-in didn't check out: {Reason}", result.Failure?.Message);
            return new PasskeySignIn(PasskeyCheck.Failed, null);
        }

        var user = result.User!;

        // The signature counter and backup state the device reported this time. The sign-in holds
        // whether or not this is saved: a save that lost a race with another one only misses them.
        var saved = await userManager.AddOrUpdatePasskeyAsync(user, result.Passkey!);
        if (!saved.Succeeded)
        {
            logger.LogWarning(
                "A passkey's counter for {UserId} wasn't saved: {Errors}",
                user.Id,
                string.Join("; ", saved.Errors.Select(e => e.Description)));
        }

        // As the account is now: gone meanwhile, it signs nobody in.
        return await identityService.FindByIdAsync(user.Id, cancellationToken) is { } profile
            ? new PasskeySignIn(PasskeyCheck.Passed, profile)
            : new PasskeySignIn(PasskeyCheck.Unknown, null);
    }

    // The request, and the address of the page it came from, when the app is served there. A
    // browser always says where a POST comes from.
    private (HttpContext Context, string Origin) Request()
    {
        var context = httpContextAccessor.HttpContext
            ?? throw new InvalidOperationException("Passkeys are only used within a request.");
        var origin = origins.Find(context.Request.Headers.Origin.ToString());
        return origin is null ? throw new ForbiddenAccessException(Messages.Passkey_UnknownOrigin) : (context, origin);
    }

    private PasskeyHandler<ApplicationUser> HandlerFor(string origin) =>
        new(userManager, Options.Create(new IdentityPasskeyOptions
        {
            // The passkey belongs to the domain of the page's address: on another, it isn't offered.
            ServerDomain = new Uri(origin).IdnHost,
            AuthenticatorTimeout = PasskeyRules.ChallengeLifetime,
            // Kept on the device, so signing in needs no email first...
            ResidentKeyRequirement = "required",
            // ...and the device checks who's holding it: Face ID, a fingerprint, a PIN.
            UserVerificationRequirement = "required",
            // The answer comes from that same page, never from a frame inside another site.
            ValidateOrigin = context => ValueTask.FromResult(!context.CrossOrigin && context.Origin == origin),
        }));

    // What checking the answer needs, carried by the browser: which ceremony, for whom, from where,
    // and the handler's state, which holds the challenge. Encrypted and signed, so it can't be read
    // or changed on the way.
    private sealed record Challenge(string Id, string Ceremony, Guid? UserId, string Origin, string? State, DateTimeOffset GoodUntil);

    private string Protect(string ceremony, Guid? userId, string origin, string? state)
    {
        var challenge = new Challenge(
            Guid.NewGuid().ToString("N"), ceremony, userId, origin, state, timeProvider.GetUtcNow() + PasskeyRules.ChallengeLifetime);
        return _protector.Protect(JsonSerializer.Serialize(challenge));
    }

    // The challenge a token stands for, when SmartDesk made it, for this ceremony from this address,
    // and it's in time and not answered yet - which, from now on, it is.
    private Challenge? Open(string token, string ceremony, string origin)
    {
        Challenge? challenge;
        try
        {
            challenge = JsonSerializer.Deserialize<Challenge>(_protector.Unprotect(token));
        }
        catch (Exception e) when (e is CryptographicException or FormatException or JsonException)
        {
            return null;
        }

        return challenge is not null
            && challenge.Ceremony == ceremony
            && challenge.Origin == origin
            && challenge.GoodUntil > timeProvider.GetUtcNow()
            && challengeLog.TryUse(challenge.Id, challenge.GoodUntil)
                ? challenge
                : null;
    }

    // The id of the credential the browser answered with, base64url in its JSON.
    private static byte[]? CredentialIdOf(string credentialJson)
    {
        try
        {
            using var document = JsonDocument.Parse(credentialJson);
            return document.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                ? FromBase64Url(id.GetString()!)
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static byte[]? FromBase64Url(string value)
    {
        try
        {
            return value.Length > 0 ? WebEncoders.Base64UrlDecode(value) : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static PasskeyDto ToDto(UserPasskeyInfo passkey) =>
        new(WebEncoders.Base64UrlEncode(passkey.CredentialId), passkey.Name ?? string.Empty, passkey.CreatedAt, passkey.IsBackedUp);

    private static void Ensure(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Identity failed to {operation}: {string.Join("; ", result.Errors.Select(e => e.Description))}");
        }
    }
}
