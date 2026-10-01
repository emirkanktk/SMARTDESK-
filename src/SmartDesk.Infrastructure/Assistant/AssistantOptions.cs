namespace SmartDesk.Infrastructure.Assistant;

// The assistant's language model (ADR-0029). Without an ApiKey the assistant is off and the
// rest of the API works as before.
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    // Never in appsettings - dotnet user-secrets locally, an environment variable elsewhere
    // (Assistant__ApiKey), like the JWT key (ADR-0005).
    public string ApiKey { get; init; } = string.Empty;

    public string Model { get; init; } = "claude-opus-5-5";

    // How hard the model thinks before answering: low, medium, high, xhigh or max. A booking
    // chat is simple; low keeps answers quick and cheap.
    public string Effort { get; init; } = "low";

    public int MaxTokens { get; init; } = 4096;

    // Rounds of tool calls in one answer before the model has to reply with what it has.
    public int MaxToolRounds { get; init; } = 6;

    // How long one answer may take, all its rounds included. Past that the user is asked to try
    // again (503) instead of being kept waiting.
    public int TimeoutSeconds { get; init; } = 90;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
