using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SmartDesk.Application.Common.Exceptions;
using SmartDesk.Application.Common.Interfaces;
using SmartDesk.Application.Resources;
using SmartDesk.Infrastructure;
using SmartDesk.Infrastructure.Assistant;

namespace SmartDesk.Tests.Assistant;

// The Gemini adapter (ADR-0036) against a stand-in for the generateContent API: what it sends,
// and what it does with the answers. No network, no key, nothing billed.
public sealed class GeminiAssistantModelTests
{
    private const string Instructions = "You are the assistant inside SmartDesk.";
    private const string Context = "Now: Monday 2026-10-05 09:00 in UTC (UTC+00:00).";

    // Real time a test waits for an answer that should already have ended.
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly FakeGeminiApi _api = new();
    private readonly RecordingToolbox _toolbox = new();
    private readonly FakeTimeProvider _time = new();
    private readonly GeminiAssistantModel _model;

    public GeminiAssistantModelTests() =>
        _model = Model(new AssistantOptions { Provider = "Gemini", ApiKey = "test-key", MaxToolRounds = 2, TimeoutSeconds = 30 });

    private GeminiAssistantModel Model(AssistantOptions options) =>
        new(Options.Create(options), new FakeClientFactory(_api), NullLogger<GeminiAssistantModel>.Instance, _time);

    private Task<string> AskAsync(params AssistantMessage[] conversation) =>
        _model.AnswerAsync(new AssistantRequest(Instructions, Context, conversation), _toolbox, CancellationToken.None);

    private static AssistantMessage User(string text) => new(AssistantRole.User, text);

    private static AssistantMessage Assistant(string text) => new(AssistantRole.Assistant, text);

    private static object Text(string text) => new { text };

    private static object Call(string name, object args, string signature = "sig-1") =>
        new { functionCall = new { id = "call-1", name, args }, thoughtSignature = signature };

    private static string Answer(params object[] parts) => Answer("STOP", parts);

    private static string Answer(string finishReason, params object[] parts) => JsonSerializer.Serialize(new
    {
        candidates = new[] { new { content = new { role = "model", parts }, finishReason } },
        usageMetadata = new { promptTokenCount = 120, candidatesTokenCount = 40, totalTokenCount = 160 },
    });

    [Fact]
    public async Task A_reply_is_returned_as_its_text()
    {
        _api.Answer(Answer(Text("Room A is free at 10:00.")));

        var answer = await AskAsync(User("Is Room A free tomorrow at 10?"));

        answer.Should().Be("Room A is free at 10:00.");
        _toolbox.Ran.Should().BeEmpty();
    }

    [Fact]
    public async Task It_sends_the_model_the_key_the_instructions_the_functions_and_the_conversation()
    {
        _api.Answer(Answer(Text("Hello!")));

        await AskAsync(User("Hi"));

        var (request, body) = _api.Requests.Should().ContainSingle().Subject;
        request.RequestUri!.AbsoluteUri.Should().Be(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-flash-latest:generateContent");
        request.Headers.GetValues("x-goog-api-key").Should().Equal("test-key");
        request.RequestUri.Query.Should().BeEmpty("the key never goes in the address");

        var system = body.GetProperty("systemInstruction").GetProperty("parts");
        system[0].GetProperty("text").GetString().Should().Be(Instructions);
        system[1].GetProperty("text").GetString().Should().Be(Context);
        body.GetProperty("toolConfig").GetProperty("functionCallingConfig").GetProperty("mode").GetString().Should().Be("AUTO");
        body.GetProperty("generationConfig").GetProperty("maxOutputTokens").GetInt32().Should().Be(4096);

        var declaration = body.GetProperty("tools")[0].GetProperty("functionDeclarations").EnumerateArray().Should().ContainSingle().Subject;
        declaration.GetProperty("name").GetString().Should().Be(RecordingToolbox.ToolName);
        declaration.GetProperty("description").GetString().Should().Be("What's free.");
        var schema = declaration.GetProperty("parametersJsonSchema");
        schema.GetProperty("type").GetString().Should().Be("object");
        schema.GetProperty("properties").GetProperty("start").GetProperty("format").GetString().Should().Be("date-time");
        schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()).Should().Equal("start");

        var content = body.GetProperty("contents").EnumerateArray().Should().ContainSingle().Subject;
        content.GetProperty("role").GetString().Should().Be("user");
        content.GetProperty("parts")[0].GetProperty("text").GetString().Should().Be("Hi");
    }

    [Fact]
    public async Task The_apps_conversation_goes_as_alternating_turns_from_the_user()
    {
        _api.Answer(Answer(Text("Done.")));

        await AskAsync(Assistant("Hi! What can I find for you?"), User("A room for 4"), User("tomorrow at 10"), Assistant("Room A?"), User("Yes"));

        var turns = _api.Requests[0].Body.GetProperty("contents").EnumerateArray()
            .Select(c => (c.GetProperty("role").GetString(), c.GetProperty("parts")[0].GetProperty("text").GetString()))
            .ToList();
        turns.Should().Equal(("user", "A room for 4\n\ntomorrow at 10"), ("model", "Room A?"), ("user", "Yes"));
    }

    [Fact]
    public async Task A_function_call_is_run_as_the_user_and_its_result_goes_back_with_the_models_turn()
    {
        _api.Answer(Answer(Text("Let me look."), Call(RecordingToolbox.ToolName, new { start = "2026-10-06T10:00" })));
        _api.Answer(Answer(Text("Room A is free.")));

        var answer = await AskAsync(User("A room tomorrow at 10"));

        answer.Should().Be("Room A is free.");
        _toolbox.Ran.Should().Equal((RecordingToolbox.ToolName, """{"start":"2026-10-06T10:00"}"""));

        var contents = _api.Requests[1].Body.GetProperty("contents");
        contents.GetArrayLength().Should().Be(3);
        // The model's turn exactly as it came, its thought signature with it.
        var echoed = contents[1];
        echoed.GetProperty("role").GetString().Should().Be("model");
        echoed.GetProperty("parts")[1].GetProperty("thoughtSignature").GetString().Should().Be("sig-1");
        echoed.GetProperty("parts")[1].GetProperty("functionCall").GetProperty("name").GetString().Should().Be(RecordingToolbox.ToolName);
        // Then the result, as the user's turn.
        var result = contents[2];
        result.GetProperty("role").GetString().Should().Be("user");
        var response = result.GetProperty("parts")[0].GetProperty("functionResponse");
        response.GetProperty("name").GetString().Should().Be(RecordingToolbox.ToolName);
        response.GetProperty("id").GetString().Should().Be("call-1");
        response.GetProperty("response").GetProperty("count").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task A_result_that_isnt_an_object_goes_back_under_output()
    {
        _toolbox.Result = """[{"name":"Room A"}]""";
        _api.Answer(Answer(Call(RecordingToolbox.ToolName, new { start = "2026-10-06T10:00" })));
        _api.Answer(Answer(Text("Room A.")));

        await AskAsync(User("A room"));

        var response = _api.Requests[1].Body.GetProperty("contents")[2].GetProperty("parts")[0].GetProperty("functionResponse").GetProperty("response");
        response.GetProperty("output")[0].GetProperty("name").GetString().Should().Be("Room A");
    }

    [Fact]
    public async Task The_last_round_may_not_call_functions_and_answers_with_what_it_has()
    {
        _api.Answer(Answer(Call(RecordingToolbox.ToolName, new { start = "a" })));
        _api.Answer(Answer(Call(RecordingToolbox.ToolName, new { start = "b" })));
        _api.Answer(Answer(Text("This is what I found.")));

        var answer = await AskAsync(User("Look everywhere"));

        answer.Should().Be("This is what I found.");
        _api.Requests.Select(r => r.Body.GetProperty("toolConfig").GetProperty("functionCallingConfig").GetProperty("mode").GetString())
            .Should().Equal("AUTO", "AUTO", "NONE");
        _toolbox.Ran.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT"}}""")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"code":403,"message":"Permission denied.","status":"PERMISSION_DENIED"}}""")]
    [InlineData(HttpStatusCode.TooManyRequests, """{"error":{"code":429,"message":"Quota exceeded.","status":"RESOURCE_EXHAUSTED"}}""")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "not json")]
    public async Task A_refused_or_failed_request_says_the_assistant_cant_answer_now(HttpStatusCode status, string body)
    {
        _api.Answer(body, status);

        var act = () => AskAsync(User("Hi"));

        (await act.Should().ThrowAsync<ServiceUnavailableException>()).Which.Message.Should().Be(Messages.Assistant_Unavailable);
    }

    [Fact]
    public async Task A_model_that_cant_be_reached_says_the_same()
    {
        _api.Fail();

        var act = () => AskAsync(User("Hi"));

        (await act.Should().ThrowAsync<ServiceUnavailableException>()).Which.Message.Should().Be(Messages.Assistant_Unavailable);
    }

    [Theory]
    [InlineData("SAFETY")]
    [InlineData("PROHIBITED_CONTENT")]
    public async Task An_answer_withheld_on_policy_grounds_says_so(string reason)
    {
        _api.Answer(Answer(reason));

        (await AskAsync(User("Something it won't answer"))).Should().Be(Messages.Assistant_Refused);
    }

    [Fact]
    public async Task A_question_blocked_before_any_answer_says_so_too()
    {
        _api.Answer("""{"promptFeedback":{"blockReason":"SAFETY"}}""");

        (await AskAsync(User("Something it won't read"))).Should().Be(Messages.Assistant_Refused);
    }

    [Fact]
    public async Task An_answer_with_no_words_is_one_it_couldnt_give()
    {
        _api.Answer(Answer("MAX_TOKENS"));

        (await AskAsync(User("Hi"))).Should().Be(Messages.Assistant_Unavailable);
    }

    [Fact]
    public async Task Past_the_time_limit_the_user_is_asked_to_try_again()
    {
        _api.Hang();

        var asking = AskAsync(User("Hi"));
        await _api.Waiting.WaitAsync(Patience);
        _time.Advance(TimeSpan.FromSeconds(31));

        (await asking.Invoking(a => a).Should().ThrowAsync<ServiceUnavailableException>().WaitAsync(Patience))
            .Which.Message.Should().Be(Messages.Assistant_Unavailable);
    }

    [Fact]
    public async Task Without_a_key_it_is_off()
    {
        var model = Model(new AssistantOptions { Provider = "Gemini" });

        model.IsConfigured.Should().BeFalse();
        var act = () => model.AnswerAsync(new AssistantRequest(Instructions, Context, [User("Hi")]), _toolbox, CancellationToken.None);
        (await act.Should().ThrowAsync<ServiceUnavailableException>()).Which.Message.Should().Be(Messages.Assistant_NotConfigured);
        _api.Requests.Should().BeEmpty();
    }

    // --- Choosing the provider ---

    private static ServiceProvider Services(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=unused",
                ["Jwt:Issuer"] = "SmartDesk",
                ["Jwt:Audience"] = "SmartDesk",
                ["Jwt:Key"] = new string('k', 32),
                ["App:FrontendUrl"] = "http://localhost:4200",
            }.Concat(settings).ToDictionary())
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new FakeHostEnvironment());
        services.AddInfrastructure(configuration);
        return services.BuildServiceProvider();
    }

    [Theory]
    [InlineData(null, typeof(ClaudeAssistantModel))]
    [InlineData("Anthropic", typeof(ClaudeAssistantModel))]
    [InlineData("gemini", typeof(GeminiAssistantModel))]
    public void The_provider_setting_picks_the_model(string? provider, Type expected)
    {
        var settings = new Dictionary<string, string?> { ["Assistant:ApiKey"] = "test-key" };
        if (provider is not null)
        {
            settings["Assistant:Provider"] = provider;
        }

        using var services = Services(settings);
        services.GetRequiredService<IAssistantModel>().Should().BeOfType(expected);
    }

    [Fact]
    public void An_unknown_provider_is_refused_at_startup()
    {
        using var services = Services(new Dictionary<string, string?> { ["Assistant:Provider"] = "OpenAI" });

        var act = () => services.GetRequiredService<IOptions<AssistantOptions>>().Value;

        act.Should().Throw<OptionsValidationException>().WithMessage("*Assistant:Provider must be Anthropic or Gemini*");
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "SmartDesk.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    private sealed class FakeClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeGeminiApi : HttpMessageHandler
    {
        // null: a request that gets no answer until it's cancelled; Failure: no connection.
        private readonly Queue<(string Json, HttpStatusCode Status)?> _responses = new();
        private readonly TaskCompletionSource _waiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _fail;

        public List<(HttpRequestMessage Request, JsonElement Body)> Requests { get; } = [];

        public Task Waiting => _waiting.Task;

        public void Answer(string json, HttpStatusCode status = HttpStatusCode.OK) => _responses.Enqueue((json, status));

        public void Hang() => _responses.Enqueue(null);

        public void Fail() => _fail = true;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add((request, JsonSerializer.Deserialize<JsonElement>(body)));
            if (_fail)
            {
                throw new HttpRequestException("No route to host.");
            }

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

        public string Result { get; set; } = """{"count":1,"resources":[{"name":"Room A"}]}""";

        public List<(string Name, string Input)> Ran { get; } = [];

        public IReadOnlyList<AssistantTool> Tools { get; } =
        [
            new(ToolName, "What's free.",
                new Dictionary<string, JsonElement> { ["start"] = JsonSerializer.SerializeToElement(new { type = "string", format = "date-time" }) },
                ["start"]),
        ];

        public Task<string> RunAsync(string name, JsonElement input, CancellationToken cancellationToken)
        {
            Ran.Add((name, input.GetRawText()));
            return Task.FromResult(Result);
        }
    }
}
