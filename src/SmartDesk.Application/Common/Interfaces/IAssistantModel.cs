using System.Text.Json;

namespace SmartDesk.Application.Common.Interfaces;

// The language model behind the assistant (ADR-0029). The Application hands it the
// instructions, the conversation and a toolbox; the model may call the tools, a few rounds at
// most, and answers in words. Whatever the provider needs between those rounds (its own
// message format, tool-call ids, thinking) stays inside the implementation.
public interface IAssistantModel
{
    // False when no API key is configured: the assistant is switched off and the app leaves it out.
    bool IsConfigured { get; }

    // The answer to the conversation's last user message. Throws ServiceUnavailableException
    // when the provider can't be reached or refuses the request.
    Task<string> AnswerAsync(AssistantRequest request, IAssistantToolbox toolbox, CancellationToken cancellationToken);
}

// Instructions: the same for every request, so the provider can cache them. Context: what
// changes (the time, the user's name and language).
public sealed record AssistantRequest(string Instructions, string Context, IReadOnlyList<AssistantMessage> Conversation);

public sealed record AssistantMessage(AssistantRole Role, string Text);

public enum AssistantRole
{
    User,
    Assistant,
}

// The tools the model may call. Each runs as the signed-in user.
public interface IAssistantToolbox
{
    IReadOnlyList<AssistantTool> Tools { get; }

    // The tool's answer for the model, as JSON. A bad input or a refused action is an answer
    // too ({ "error": "..." }), so the model can put it right; it never throws for those.
    Task<string> RunAsync(string name, JsonElement input, CancellationToken cancellationToken);
}

// A tool as the model sees it: what it's for, and the JSON schema of its input object.
public sealed record AssistantTool(
    string Name,
    string Description,
    IReadOnlyDictionary<string, JsonElement> Properties,
    IReadOnlyList<string> Required);
