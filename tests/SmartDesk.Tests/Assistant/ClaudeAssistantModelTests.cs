using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Resources;
using SmartDesk.Infrastructure.Assistant;

namespace SmartDesk.Tests.Assistant;

// The Claude adapter (ADR-0029) against a stand-in for the Messages API: what it sends, and what
// it does with the answers. No network, no key, nothing billed.
public sealed class ClaudeAssistantModelTests : IDisposable
{
    private const string Instructions = "You are the assistant inside SmartDesk.";
    private const string Context = "Now: Monday 2026-10-05 09:00 in UTC (UTC+00:00).";

    // Real time a test waits for an answer that should already have ended, so that a regression
    // fails the test instead of hanging it.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly FakeMessagesApi _api = new();
    private readonly RecordingToolbox _toolbox = new();
    private readonly FakeTimeProvider _time = new();
    private readonly ClaudeAssistantModel _model;

    public ClaudeAssistantModelTests() =>
        _model = Model(new AssistantOptions { ApiKey = "test-key", MaxToolRounds = 2, TimeoutSeconds = 30 });

    public void Dispose() => _model.Dispose();

    private ClaudeAssistantModel Model(AssistantOptions options) =>
        new(Options.Create(options), NullLogger<ClaudeAssistantModel>.Instance, _time, new HttpClient(_api));

    private Task<string> AskAsync(params AssistantMessage[] conversation) =>
        _model.AnswerAsync(new AssistantRequest(Instructions, Context, conversation), _toolbox, CancellationToken.None);

    private static AssistantMessage User(string text) => new(AssistantRole.User, text);

    private static AssistantMessage Assistant(string text) => new(AssistantRole.Assistant, text);

    private static object Text(string text) => new { type = "text", text };

    private static object Thinking(string signature) => new { type = "thinking", thinking = "", signature };

    private static object ToolUse(string id, string name, object input) => new { type = "tool_use", id, name, input };

    private static string Message(string stopReason, params object[] content) => JsonSerializer.Serialize(new
    {
        id = "msg_" + Guid.NewGuid().ToString("N"),
        type = "message",
        role = "assistant",
        model = "claude-opus-5-5",
        content,
        stop_reason = stopReason,
        stop_sequence = (string?)null,
        usage = new { input_tokens = 120, output_tokens = 40, cache_creation_input_tokens = 0, cache_read_input_tokens = 900 },
    });

    private static IEnumerable<string?> Types(JsonElement blocks) =>
        blocks.EnumerateArray().Select(b => b.GetProperty("type").GetString());

    [Fact]
    public async Task AnswerAsync_Reply_ReturnsItsText()
    {
        _api.Answer(Message("end_turn", Thinking("sig-1"), Text("Room A is free at 10:00.")));

        var answer = await AskAsync(User("Is Room A free tomorrow at 10?"));

        answer.Should().Be("Room A is free at 10:00.");
        _toolbox.Ran.Should().BeEmpty();
    }

    [Fact]
    public async Task AnswerAsync_SendsTheModelInstructionsToolsAndConversation()
    {
        _api.Answer(Message("end_turn", Text("Hello!")));

        await AskAsync(User("Hi"));

        var (request, body) = _api.Requests.Should().ContainSingle().Subject;
        request.RequestUri!.AbsolutePath.Should().Be("/v1/messages");
        request.Headers.GetValues("x-api-key").Should().Equal("test-key");
        request.Headers.GetValues("anthropic-beta").Should().Contain(v => v.Contains("server-side-fallback-2026-07-01"));

        body.GetProperty("model").GetString().Should().Be("claude-opus-5-5");
        body.GetProperty("max_tokens").GetInt32().Should().Be(4096);
        body.GetProperty("output_config").GetProperty("effort").GetString().Should().Be("low");
        body.GetProperty("tool_choice").GetProperty("type").GetString().Should().Be("auto");
        body.TryGetProperty("fallbacks", out _).Should().BeTrue();
        // The instructions are cached; the context after them, which changes, isn't.
        var system = body.GetProperty("system");
        system[0].GetProperty("text").GetString().Should().Be(Instructions);
        system[0].GetProperty("cache_control").GetProperty("type").GetString().Should().Be("ephemeral");
        system[1].GetProperty("text").GetString().Should().Be(Context);
        system[1].TryGetProperty("cache_control", out _).Should().BeFalse();
        body.GetProperty("cache_control").GetProperty("type").GetString().Should().Be("ephemeral");

        var tool = body.GetProperty("tools").EnumerateArray().Should().ContainSingle().Subject;
        tool.GetProperty("name").GetString().Should().Be(RecordingToolbox.ToolName);
        tool.GetProperty("input_schema").GetProperty("type").GetString().Should().Be("object");
        tool.GetProperty("input_schema").GetProperty("properties").GetProperty("start").GetProperty("type").GetString().Should().Be("string");
        tool.GetProperty("input_schema").GetProperty("required").EnumerateArray().Select(r => r.GetString()).Should().Equal("start");

        var message = body.GetProperty("messages").EnumerateArray().Should().ContainSingle().Subject;
        message.GetProperty("role").GetString().Should().Be("user");
        message.GetProperty("content").GetString().Should().Be("Hi");
    }

    [Fact]
    public async Task AnswerAsync_ConversationFromTheApp_IsSentAsAlternatingTurnsFromTheUser()
    {
        _api.Answer(Message("end_turn", Text("Done.")));

        await AskAsync(Assistant("Hi! What can I find for you?"), User("A room for 4"), User("tomorrow at 10"), Assistant("Room A?"), User("Yes"));

        var messages = _api.Requests.Single().Body.GetProperty("messages").EnumerateArray()
            .Select(m => (m.GetProperty("role").GetString(), m.GetProperty("content").GetString()));
        messages.Should().Equal(("user", "A room for 4\n\ntomorrow at 10"), ("assistant", "Room A?"), ("user", "Yes"));
    }

    [Fact]
    public async Task AnswerAsync_ToolCall_RunsItAndContinuesTheSameTurn()
    {
        _api.Answer(Message("tool_use",
            Thinking("sig-1"), Text("Let me check."), ToolUse("toolu_1", RecordingToolbox.ToolName, new { start = "2026-10-06T10:00" })));
        _api.Answer(Message("end_turn", Thinking("sig-2"), Text("Room A is free.")));

        var answer = await AskAsync(User("Anything free tomorrow at 10?"));

        answer.Should().Be("Room A is free.");
        _toolbox.Ran.Should().ContainSingle().Which.Should().Be((RecordingToolbox.ToolName, """{"start":"2026-10-06T10:00"}"""));

        var messages = _api.Requests[1].Body.GetProperty("messages");
        messages.GetArrayLength().Should().Be(3);
        // The assistant's turn goes back as it came, thinking and its signature included.
        var echoed = messages[1];
        echoed.GetProperty("role").GetString().Should().Be("assistant");
        Types(echoed.GetProperty("content")).Should().Equal("thinking", "text", "tool_use");
        echoed.GetProperty("content")[0].GetProperty("signature").GetString().Should().Be("sig-1");
        echoed.GetProperty("content")[2].GetProperty("id").GetString().Should().Be("toolu_1");
        var result = messages[2].GetProperty("content").EnumerateArray().Should().ContainSingle().Subject;
        result.GetProperty("type").GetString().Should().Be("tool_result");
        result.GetProperty("tool_use_id").GetString().Should().Be("toolu_1");
        result.GetProperty("content").GetString().Should().Be(RecordingToolbox.Result);
    }

    [Fact]
    public async Task AnswerAsync_SeveralToolCallsAtOnce_AreAnsweredInOneTurn()
    {
        _api.Answer(Message("tool_use",
            ToolUse("toolu_1", RecordingToolbox.ToolName, new { start = "2026-10-06T10:00" }),
            ToolUse("toolu_2", RecordingToolbox.ToolName, new { start = "2026-10-06T14:00" })));
        _api.Answer(Message("end_turn", Text("Both are free.")));

        await AskAsync(User("10 or 14?"));

        _toolbox.Ran.Should().HaveCount(2);
        var results = _api.Requests[1].Body.GetProperty("messages")[2].GetProperty("content");
        results.EnumerateArray().Select(r => r.GetProperty("tool_use_id").GetString()).Should().Equal("toolu_1", "toolu_2");
    }

    [Fact]
    public async Task AnswerAsync_ToolRoundsRunOut_TheLastRoundMustAnswer()
    {
        var call = Message("tool_use", ToolUse("toolu_1", RecordingToolbox.ToolName, new { start = "2026-10-06T10:00" }));
        _api.Answer(call);
        _api.Answer(call);
        _api.Answer(Message("end_turn", Text("Here's what I found.")));

        var answer = await AskAsync(User("Look everywhere."));

        answer.Should().Be("Here's what I found.");
        _api.Requests.Select(r => r.Body.GetProperty("tool_choice").GetProperty("type").GetString())
            .Should().Equal("auto", "auto", "none");
    }

    [Fact]
    public async Task AnswerAsync_FallbackModelTookOver_TheBoundaryIsSentBack()
    {
        var fallback = new
        {
            type = "fallback",
            from = new { model = "claude-opus-5-5" },
            to = new { model = "claude-opus-5" },
            trigger = new { type = "refusal", category = "cyber" },
        };
        _api.Answer(Message("tool_use",
            Thinking("sig-1"), fallback, Thinking("sig-2"), ToolUse("toolu_1", RecordingToolbox.ToolName, new { start = "2026-10-06T10:00" })));
        _api.Answer(Message("end_turn", Text("Room A is free.")));

        await AskAsync(User("Anything free tomorrow at 10?"));

        var echoed = _api.Requests[1].Body.GetProperty("messages")[1].GetProperty("content");
        Types(echoed).Should().Equal("thinking", "fallback", "thinking", "tool_use");
        echoed[1].GetProperty("from").GetProperty("model").GetString().Should().Be("claude-opus-5-5");
        echoed[1].GetProperty("to").GetProperty("model").GetString().Should().Be("claude-opus-5");
        echoed[1].GetProperty("trigger").GetProperty("type").GetString().Should().Be("refusal");
        echoed[1].GetProperty("trigger").GetProperty("category").GetString().Should().Be("cyber");
    }

    [Fact]
    public async Task AnswerAsync_Refusal_SaysItCantHelpWithThat()
    {
        _api.Answer(Message("refusal"));

        var answer = await AskAsync(User("Something off-limits"));

        answer.Should().Be(Messages.Assistant_Refused);
    }

    [Fact]
    public async Task AnswerAsync_NoText_SaysItCantAnswerNow()
    {
        _api.Answer(Message("end_turn", Thinking("sig-1")));

        var answer = await AskAsync(User("Hi"));

        answer.Should().Be(Messages.Assistant_Unavailable);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "authentication_error")]
    [InlineData(HttpStatusCode.BadRequest, "invalid_request_error")]
    public async Task AnswerAsync_ApiRefusesTheRequest_IsUnavailable(HttpStatusCode status, string error)
    {
        _api.Answer(JsonSerializer.Serialize(new { type = "error", error = new { type = error, message = "No." } }), status);

        var act = () => AskAsync(User("Hi"));

        await act.Should().ThrowAsync<ServiceUnavailableException>().WithMessage(Messages.Assistant_Unavailable);
    }

    [Fact]
    public async Task AnswerAsync_NoAnswerInTime_IsUnavailable()
    {
        _api.Hang();

        var asking = AskAsync(User("Hi"));
        await _api.Waiting;
        _time.Advance(TimeSpan.FromSeconds(30));

        var act = () => asking.WaitAsync(Patience);
        await act.Should().ThrowAsync<ServiceUnavailableException>().WithMessage(Messages.Assistant_Unavailable);
    }

    [Fact]
    public async Task AnswerAsync_TheTimeLimitIsForTheWholeAnswer_NotEachCall()
    {
        _api.Answer(Message("tool_use", ToolUse("toolu_1", RecordingToolbox.ToolName, new { start = "2026-10-06T10:00" })));
        _api.Hang();
        // The first round and its tool take 20 s, then the second call hangs for 10.
        _toolbox.WhileRunning = () => _time.Advance(TimeSpan.FromSeconds(20));

        var asking = AskAsync(User("Anything free tomorrow at 10?"));
        await _api.Waiting;
        _time.Advance(TimeSpan.FromSeconds(10));

        var act = () => asking.WaitAsync(Patience);
        await act.Should().ThrowAsync<ServiceUnavailableException>().WithMessage(Messages.Assistant_Unavailable);
        _api.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task AnswerAsync_CallerGone_IsCancelledRatherThanUnavailable()
    {
        _api.Hang();
        using var leaving = new CancellationTokenSource();

        var asking = _model.AnswerAsync(new AssistantRequest(Instructions, Context, [User("Hi")]), _toolbox, leaving.Token);
        await _api.Waiting;
        await leaving.CancelAsync();

        var act = () => asking.WaitAsync(Patience);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AnswerAsync_NoKey_IsUnavailableAndCallsNothing()
    {
        using var model = Model(new AssistantOptions());

        var act = () => model.AnswerAsync(new AssistantRequest(Instructions, Context, [User("Hi")]), _toolbox, CancellationToken.None);

        model.IsConfigured.Should().BeFalse();
        await act.Should().ThrowAsync<ServiceUnavailableException>().WithMessage(Messages.Assistant_NotConfigured);
        _api.Requests.Should().BeEmpty();
    }

    // Answers each request with the next queued response, and keeps what was sent.
    private sealed class FakeMessagesApi : HttpMessageHandler
    {
        // null: a request that gets no answer until it's cancelled.
        private readonly Queue<(string Json, HttpStatusCode Status)?> _responses = new();
        private readonly TaskCompletionSource _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<(HttpRequestMessage Request, JsonElement Body)> Requests { get; } = [];

        // Completes when a request is left hanging.
        public Task Waiting => _waiting.Task;

        public void Answer(string json, HttpStatusCode status = HttpStatusCode.OK) => _responses.Enqueue((json, status));

        public void Hang() => _responses.Enqueue(null);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, JsonSerializer.Deserialize<JsonElement>(body)));
            if (_responses.Dequeue() is not { } response)
            {
                _waiting.SetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new UnreachableException();
            }

            return new HttpResponseMessage(response.Status) { Content = new StringContent(response.Json, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class RecordingToolbox : IAssistantToolbox
    {
        public const string ToolName = "find_free_resources";
        public const string Result = """{"count":1,"resources":[{"name":"Room A"}]}""";

        public List<(string Name, string Input)> Ran { get; } = [];

        // Something that happens while a tool runs, such as time passing.
        public Action? WhileRunning { get; set; }

        public IReadOnlyList<AssistantTool> Tools { get; } =
        [
            new(ToolName, "What's free.",
                new Dictionary<string, JsonElement> { ["start"] = JsonSerializer.SerializeToElement(new { type = "string" }) },
                ["start"]),
        ];

        public Task<string> RunAsync(string name, JsonElement input, CancellationToken cancellationToken)
        {
            Ran.Add((name, input.GetRawText()));
            WhileRunning?.Invoke();
            return Task.FromResult(Result);
        }
    }
}
