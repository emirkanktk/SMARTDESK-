using System.Text.Json;
using SmartDesk.Application.Common.Models;

namespace SmartDesk.Application.Features.Passkeys.Common;

// What the browser's navigator.credentials.create() or get() takes, in the JSON form
// PublicKeyCredential.parseCreationOptionsFromJSON and parseRequestOptionsFromJSON read, and the
// token that goes back with the answer (ADR-0057).
public sealed record PasskeyOptionsDto(JsonElement Options, string Token)
{
    public static PasskeyOptionsDto From(PasskeyChallenge challenge) =>
        new(JsonSerializer.Deserialize<JsonElement>(challenge.OptionsJson), challenge.Token);
}
