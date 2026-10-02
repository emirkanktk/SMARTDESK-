using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Resources;

namespace SmartDesk.Infrastructure.Assistant;

// The assistant on Google's Gemini API (ADR-0036), over its REST generateContent endpoint. The
// same loop as Claude's: Gemini either replies, or asks for functions; the toolbox runs them as
// the signed-in user and the results go back, until it replies, the rounds run out or the time
// limit does.
public sealed class GeminiAssistantModel(
    IOptions<AssistantOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<GeminiAssistantModel> logger,
    TimeProvider timeProvider) : IAssistantModel
{
    public const string HttpClientName = "gemini";

    public static readonly Uri BaseAddress = new("https://generativelanguage.googleapis.com/v1beta/");

    // Reasons Gemini gives for not answering on policy grounds.
    private static readonly HashSet<string> Declined = ["SAFETY", "RECITATION", "BLOCKLIST", "PROHIBITED_CONTENT", "SPII"];

    private readonly AssistantOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public async Task<string> AnswerAsync(AssistantRequest request, IAssistantToolbox toolbox, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new ServiceUnavailableException(Messages.Assistant_NotConfigured);
        }

        // One time limit for the whole answer, every round and the tools in between.
        using var timeLimit = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds), timeProvider);
        using var answer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeLimit.Token);
        try
        {
            return await AnswerInRoundsAsync(request, toolbox, answer.Token);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning(e, "The assistant didn't answer within {Seconds} s.", _options.TimeoutSeconds);
            throw new ServiceUnavailableException(Messages.Assistant_Unavailable);
        }
    }

    private async Task<string> AnswerInRoundsAsync(AssistantRequest request, IAssistantToolbox toolbox, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();
        var contents = ToContents(request.Conversation);
        var declarations = new JsonArray([.. toolbox.Tools.Select(ToDeclaration)]);
        var usage = new Usage();

        for (var round = 0; ; round++)
        {
            // The last round may not call functions: it answers with what it has.
            var lastRound = round >= _options.MaxToolRounds;
            var response = await GenerateAsync(Body(request, contents, declarations, lastRound), cancellationToken);
            usage.Add(response["usageMetadata"]);

            if (DeclineReason(response) is { } reason)
            {
                logger.LogWarning("The assistant's model declined to answer: {Reason}.", reason);
                return Messages.Assistant_Refused;
            }

            var content = response["candidates"]?[0]?["content"];
            var parts = content?["parts"] as JsonArray ?? [];
            var calls = parts.Select(part => part?["functionCall"]).OfType<JsonObject>().ToList();
            if (calls.Count == 0 || lastRound)
            {
                logger.LogInformation(
                    "The assistant answered in {Rounds} round(s) and {Seconds:0.0} s: {InputTokens} input tokens ({CachedTokens} read from the cache), {OutputTokens} output tokens.",
                    round + 1, timeProvider.GetElapsedTime(started).TotalSeconds, usage.Input, usage.Cached, usage.Output);
                return AnswerText(parts);
            }

            // The model's turn as it came - its thought signatures with it, which a thinking model
            // checks on the next round - then every function's result in one user turn.
            contents.Add(content!.DeepClone());
            var results = new JsonArray();
            foreach (var call in calls)
            {
                var name = call["name"]?.GetValue<string>() ?? string.Empty;
                var input = JsonSerializer.SerializeToElement(call["args"] ?? new JsonObject());
                var result = await toolbox.RunAsync(name, input, cancellationToken);
                var functionResponse = new JsonObject { ["name"] = name, ["response"] = ToResponse(result) };
                if (call["id"] is { } id)
                {
                    functionResponse["id"] = id.DeepClone();
                }

                results.Add(new JsonObject { ["functionResponse"] = functionResponse });
            }

            contents.Add(new JsonObject { ["role"] = "user", ["parts"] = results });
        }
    }

    private JsonObject Body(AssistantRequest request, JsonArray contents, JsonArray declarations, bool noMoreTools) => new()
    {
        ["systemInstruction"] = new JsonObject
        {
            ["parts"] = new JsonArray(new JsonObject { ["text"] = request.Instructions }, new JsonObject { ["text"] = request.Context }),
        },
        ["contents"] = contents.DeepClone(),
        ["tools"] = new JsonArray(new JsonObject { ["functionDeclarations"] = declarations.DeepClone() }),
        ["toolConfig"] = new JsonObject
        {
            ["functionCallingConfig"] = new JsonObject { ["mode"] = noMoreTools ? "NONE" : "AUTO" },
        },
        ["generationConfig"] = new JsonObject { ["maxOutputTokens"] = _options.MaxTokens },
    };

    private async Task<JsonNode> GenerateAsync(JsonObject body, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(
            HttpMethod.Post, new Uri(BaseAddress, $"models/{Uri.EscapeDataString(_options.GeminiModel)}:generateContent"));
        // In a header rather than the address, so the key stays out of every log.
        request.Headers.Add("x-goog-api-key", _options.ApiKey);
        request.Content = JsonContent.Create(body);

        HttpResponseMessage response;
        try
        {
            response = await client.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException e)
        {
            logger.LogWarning(e, "The assistant's model couldn't be reached.");
            throw new ServiceUnavailableException(Messages.Assistant_Unavailable);
        }

        using (response)
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode && JsonNode.Parse(json) is { } answer)
            {
                return answer;
            }

            // Google's own words for what went wrong; they never contain the key.
            var problem = ErrorMessage(json);
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden:
                    logger.LogError("The assistant's API key was refused ({Status}): {Problem} Check Assistant:ApiKey.", (int)response.StatusCode, problem);
                    break;
                case HttpStatusCode.TooManyRequests:
                    logger.LogWarning("The assistant's model is rate limited: {Problem}", problem);
                    break;
                case >= HttpStatusCode.InternalServerError:
                    logger.LogWarning("The assistant's model failed ({Status}): {Problem}", (int)response.StatusCode, problem);
                    break;
                default:
                    // An invalid key is a 400 here, as is a request the API didn't accept.
                    logger.LogError("The assistant's request was refused ({Status}): {Problem}", (int)response.StatusCode, problem);
                    break;
            }

            throw new ServiceUnavailableException(Messages.Assistant_Unavailable);
        }
    }

    // A function as Gemini sees it, its input as a plain JSON Schema, the same as for Claude.
    private static JsonObject ToDeclaration(AssistantTool tool)
    {
        var properties = new JsonObject();
        foreach (var (name, schema) in tool.Properties)
        {
            properties[name] = JsonNode.Parse(schema.GetRawText());
        }

        return new JsonObject
        {
            ["name"] = tool.Name,
            ["description"] = tool.Description,
            ["parametersJsonSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = properties,
                ["required"] = new JsonArray([.. tool.Required.Select(required => JsonValue.Create(required))]),
            },
        };
    }

    // Alternating turns that start with the user's, as for Claude: a greeting before the first
    // question is left out, and turns of the same side in a row are joined.
    private static JsonArray ToContents(IReadOnlyList<AssistantMessage> conversation)
    {
        var contents = new JsonArray();
        string? previous = null;
        foreach (var message in conversation.SkipWhile(m => m.Role == AssistantRole.Assistant))
        {
            var role = message.Role == AssistantRole.Assistant ? "model" : "user";
            if (role == previous)
            {
                var part = contents[contents.Count - 1]!["parts"]![0]!;
                part["text"] = part["text"]!.GetValue<string>() + "\n\n" + message.Text;
                continue;
            }

            contents.Add(new JsonObject
            {
                ["role"] = role,
                ["parts"] = new JsonArray(new JsonObject { ["text"] = message.Text }),
            });
            previous = role;
        }

        return contents;
    }

    // Gemini takes an object back: a tool's object answer goes as it is - an { "error": ... }
    // included, which it reads as the function failing - and anything else under "output".
    private static JsonNode ToResponse(string result)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(result);
        }
        catch (JsonException)
        {
            parsed = JsonValue.Create(result);
        }

        return parsed as JsonObject ?? new JsonObject { ["output"] = parsed };
    }

    private static string? DeclineReason(JsonNode response)
    {
        if (response["promptFeedback"]?["blockReason"]?.GetValue<string>() is { } blocked)
        {
            return blocked;
        }

        var finish = response["candidates"]?[0]?["finishReason"]?.GetValue<string>();
        return finish is not null && Declined.Contains(finish) ? finish : null;
    }

    private static string AnswerText(JsonArray parts)
    {
        var text = string.Join(
            "\n\n",
            parts
                .Where(part => part?["thought"]?.GetValue<bool>() != true)
                .Select(part => part?["text"]?.GetValue<string>()?.Trim())
                .Where(t => !string.IsNullOrEmpty(t)));
        return text.Length > 0 ? text : Messages.Assistant_Unavailable;
    }

    private static string ErrorMessage(string json)
    {
        try
        {
            return JsonNode.Parse(json)?["error"]?["message"]?.GetValue<string>() ?? "(no message)";
        }
        catch (JsonException)
        {
            return "(no message)";
        }
    }

    // Tokens over all the rounds of one answer, for the log: what a question costs.
    private sealed class Usage
    {
        public long Input { get; private set; }

        public long Cached { get; private set; }

        public long Output { get; private set; }

        public void Add(JsonNode? usage)
        {
            Input += usage?["promptTokenCount"]?.GetValue<long>() ?? 0;
            Cached += usage?["cachedContentTokenCount"]?.GetValue<long>() ?? 0;
            Output += (usage?["candidatesTokenCount"]?.GetValue<long>() ?? 0) + (usage?["thoughtsTokenCount"]?.GetValue<long>() ?? 0);
        }
    }
}
