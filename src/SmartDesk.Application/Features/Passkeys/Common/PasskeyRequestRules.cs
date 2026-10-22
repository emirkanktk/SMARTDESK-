using System.Text.Json;
using FluentValidation;
using SmartDesk.Application.Resources;
using SmartDesk.Domain.Rules;

namespace SmartDesk.Application.Features.Passkeys.Common;

// What a browser sends back from a passkey (ADR-0057).
public static class PasskeyRequestRules
{
    // Far longer than any token the server hands out.
    private const int MaxTokenLength = 8 * 1024;

    // The PublicKeyCredential the browser gave, as its toJSON() writes it. What's in it is
    // checked against the challenge later; this only keeps out what can't be one.
    public static IRuleBuilderOptions<T, JsonElement> BrowserCredential<T>(this IRuleBuilderInitial<T, JsonElement> rule) =>
        rule.Must(credential => credential.ValueKind == JsonValueKind.Object
                && credential.GetRawText().Length <= PasskeyRules.MaxCredentialLength)
            .WithMessage(_ => Messages.Passkey_BadCredential);

    public static IRuleBuilderOptions<T, string> ChallengeToken<T>(this IRuleBuilderInitial<T, string> rule) =>
        rule.NotEmpty().MaximumLength(MaxTokenLength);
}
