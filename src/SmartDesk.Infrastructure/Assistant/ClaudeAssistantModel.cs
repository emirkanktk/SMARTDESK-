using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Resources;

namespace SmartDesk.Infrastructure.Assistant;

// The assistant on Anthropic's Claude API (ADR-0029), through the official SDK. One answer is a
// loop: Claude either replies, or asks for tools; the toolbox runs them as the signed-in user
// and the results go back, until it replies, the rounds run out or the time limit does.
// httpClient is for tests, which put a fake API behind it; left out, the SDK uses its own.
public sealed class ClaudeAssistantModel(
    IOptions<AssistantOptions> options,
    ILogger<ClaudeAssistantModel> logger,
    TimeProvider timeProvider,
    HttpClient? httpClient = null) : IAssistantModel, IDisposable
{
    // If Claude declines a request on policy grounds, the API may serve it with another model
    // instead of stopping (the "default" fallback set).
    private const string FallbackBeta = "server-side-fallback-2026-07-01";

    private readonly AssistantOptions _options = options.Value;
    private readonly Lazy<AnthropicClient> _client = new(() => httpClient is null
        ? new AnthropicClient { ApiKey = options.Value.ApiKey }
        : new AnthropicClient { ApiKey = options.Value.ApiKey, HttpClient = httpClient });

    public bool IsConfigured => _options.IsConfigured;

    public async Task<string> AnswerAsync(AssistantRequest request, IAssistantToolbox toolbox, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new ServiceUnavailableException(Messages.Assistant_NotConfigured);
        }

        // One time limit for the whole answer: every round, the SDK's retries and the tools in
        // between. A limit per call would still let an answer run for rounds × attempts × limit.
        using var timeLimit = new CancellationTokenSource(TimeSpan.FromSeconds(_options.TimeoutSeconds), timeProvider);
        using var answer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeLimit.Token);
        try
        {
            return await AnswerInRoundsAsync(request, toolbox, answer.Token);
        }
        catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
        {
            // The time limit ran out, or the SDK's own timeout for a call did. The caller is
            // still waiting, so it gets an answer it can act on: try again.
            logger.LogWarning(e, "The assistant didn't answer within {Seconds} s.", _options.TimeoutSeconds);
            throw new ServiceUnavailableException(Messages.Assistant_Unavailable);
        }
    }

    public void Dispose()
    {
        if (_client.IsValueCreated)
        {
            _client.Value.Dispose();
        }
    }

    private async Task<string> AnswerInRoundsAsync(AssistantRequest request, IAssistantToolbox toolbox, CancellationToken cancellationToken)
    {
        var started = timeProvider.GetTimestamp();

        // The tools and the instructions are the same for every request and every user, and
        // come first, so they're cached; the context after them changes each time.
        List<BetaTextBlockParam> system =
        [
            new() { Text = request.Instructions, CacheControl = new BetaCacheControlEphemeral() },
            new() { Text = request.Context },
        ];
        List<BetaToolUnion> tools = [.. toolbox.Tools.Select(ToTool)];
        var messages = ToMessages(request.Conversation);
        var usage = new Usage();

        for (var round = 0; ; round++)
        {
            // The last round may not call tools: it answers with what it has.
            var lastRound = round >= _options.MaxToolRounds;
            var response = await CreateAsync(system, tools, messages, lastRound, cancellationToken);
            usage.Add(response.Usage);

            var refused = response.StopReason == "refusal";
            var toolUses = response.Content
                .Select(block => block.TryPickToolUse(out var toolUse) ? toolUse : null)
                .OfType<BetaToolUseBlock>()
                .ToList();
            if (refused || response.StopReason != "tool_use" || toolUses.Count == 0 || lastRound)
            {
                logger.LogInformation(
                    "The assistant answered in {Rounds} round(s) and {Seconds:0.0} s: {InputTokens} input tokens ({CachedTokens} read from the cache), {OutputTokens} output tokens.",
                    round + 1, timeProvider.GetElapsedTime(started).TotalSeconds, usage.Input, usage.CacheRead, usage.Output);
                return refused ? Messages.Assistant_Refused : AnswerText(response);
            }

            // Every tool the model asked for, answered together in one user turn.
            List<BetaContentBlockParam> results = [];
            foreach (var toolUse in toolUses)
            {
                var input = System.Text.Json.JsonSerializer.SerializeToElement(toolUse.Input);
                var result = await toolbox.RunAsync(toolUse.Name, input, cancellationToken);
                results.Add(new BetaToolResultBlockParam { ToolUseID = toolUse.ID, Content = result });
            }

            messages.Add(new BetaMessageParam { Role = Role.Assistant, Content = Echo(response) });
            messages.Add(new BetaMessageParam { Role = Role.User, Content = results });
        }
    }

    private async Task<BetaMessage> CreateAsync(
        List<BetaTextBlockParam> system,
        List<BetaToolUnion> tools,
        List<BetaMessageParam> messages,
        bool noMoreTools,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _client.Value.Beta.Messages.Create(
                new MessageCreateParams
                {
                    Model = _options.Model,
                    MaxTokens = _options.MaxTokens,
                    System = system,
                    Tools = tools,
                    ToolChoice = noMoreTools ? new BetaToolChoiceNone() : new BetaToolChoiceAuto(),
                    Messages = messages,
                    OutputConfig = new BetaOutputConfig { Effort = ToEffort(_options.Effort) },
                    // A second cache point, at the end: each round of tool calls reads the
                    // rounds before it from the cache.
                    CacheControl = new BetaCacheControlEphemeral(),
                    Betas = [FallbackBeta],
                    Fallbacks = new Default(),
                },
                cancellationToken);
        }
        catch (AnthropicUnauthorizedException e)
        {
            logger.LogError(e, "The assistant's API key was refused. Check Assistant:ApiKey.");
            throw new ServiceUnavailableException(Messages.Assistant_Unavailable);
        }
        catch (AnthropicRateLimitException e)
        {
            logger.LogWarning(e, "The assistant's model is rate limited.");
            throw new ServiceUnavailableException(Messages.Assistant_Unavailable);
        }
        catch (Anthropic5xxException e)
        {
            logger.LogWarning(e, "The assistant's model failed.");
            throw new ServiceUnavailableException(Messages.Assistant_Unavailable);
        }
        catch (AnthropicIOException e)
        {
            logger.LogWarning(e, "The assistant's model couldn't be reached.");
            throw new ServiceUnavailableException(Messages.Assistant_Unavailable);
        }
        catch (AnthropicApiException e)
        {
            // A request the API didn't accept - a bug here, not something to retry.
            logger.LogError(e, "The assistant's request was refused.");
            throw new ServiceUnavailableException(Messages.Assistant_Unavailable);
        }
    }

    private static BetaToolUnion ToTool(AssistantTool tool) => new BetaTool
    {
        Name = tool.Name,
        Description = tool.Description,
        InputSchema = new InputSchema
        {
            Properties = tool.Properties.ToDictionary(p => p.Key, p => p.Value),
            Required = [.. tool.Required],
        },
    };

    // The API takes alternating turns that start with the user's: the app's conversation is
    // folded into that shape (a greeting before the first question is left out).
    private static List<BetaMessageParam> ToMessages(IReadOnlyList<AssistantMessage> conversation)
    {
        List<(AssistantRole Role, string Text)> turns = [];
        foreach (var message in conversation.SkipWhile(m => m.Role == AssistantRole.Assistant))
        {
            if (turns.Count > 0 && turns[^1].Role == message.Role)
            {
                turns[^1] = (message.Role, turns[^1].Text + "\n\n" + message.Text);
            }
            else
            {
                turns.Add((message.Role, message.Text));
            }
        }

        return
        [
            .. turns.Select(turn => new BetaMessageParam
            {
                Role = turn.Role == AssistantRole.Assistant ? Role.Assistant : Role.User,
                Content = turn.Text,
            }),
        ];
    }

    // The assistant turn as the API sent it - thinking with its signature, text, tool calls
    // and a fallback note - so the next round continues the same turn.
    private static List<BetaContentBlockParam> Echo(BetaMessage response)
    {
        List<BetaContentBlockParam> blocks = [];
        foreach (var block in response.Content)
        {
            if (block.TryPickThinking(out var thinking))
            {
                blocks.Add(new BetaThinkingBlockParam { Thinking = thinking.Thinking, Signature = thinking.Signature });
            }
            else if (block.TryPickRedactedThinking(out var redacted))
            {
                blocks.Add(new BetaRedactedThinkingBlockParam { Data = redacted.Data });
            }
            else if (block.TryPickText(out var text))
            {
                blocks.Add(new BetaTextBlockParam { Text = text.Text });
            }
            else if (block.TryPickToolUse(out var toolUse))
            {
                blocks.Add(new BetaToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
            }
            else if (block.TryPickFallback(out var fallback))
            {
                blocks.Add(new BetaFallbackBlockParam
                {
                    From = new BetaFallbackInfoParam { Model = fallback.From.Model },
                    To = new BetaFallbackInfoParam { Model = fallback.To.Model },
                    Trigger = System.Text.Json.JsonSerializer.SerializeToElement(fallback.Trigger),
                });
            }
        }

        return blocks;
    }

    private static string AnswerText(BetaMessage response)
    {
        var text = string.Join(
            "\n\n",
            response.Content
                .Select(block => block.TryPickText(out var t) ? t.Text.Trim() : null)
                .Where(t => !string.IsNullOrEmpty(t)));
        return text.Length > 0 ? text : Messages.Assistant_Unavailable;
    }

    private static Effort ToEffort(string effort) => effort.Trim().ToLowerInvariant() switch
    {
        "medium" => Effort.Medium,
        "high" => Effort.High,
        "xhigh" => Effort.Xhigh,
        "max" => Effort.Max,
        _ => Effort.Low,
    };

    // Tokens over all the rounds of one answer, for the log: what a question costs.
    private sealed class Usage
    {
        public long Input { get; private set; }

        public long CacheRead { get; private set; }

        public long Output { get; private set; }

        public void Add(BetaUsage usage)
        {
            Input += usage.InputTokens + (usage.CacheReadInputTokens ?? 0) + (usage.CacheCreationInputTokens ?? 0);
            CacheRead += usage.CacheReadInputTokens ?? 0;
            Output += usage.OutputTokens;
        }
    }
}
