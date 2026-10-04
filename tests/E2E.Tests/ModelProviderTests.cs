// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace E2E.Tests;

public sealed class ModelProviderTests
{
    [Fact]
    public async Task Responses_model_sends_the_conversation_without_storage_and_reads_function_calls()
    {
        var handler = new RecordingHandler("""
            {
              "output": [
                { "type": "reasoning", "summary": [] },
                { "type": "message", "content": [{ "type": "output_text", "text": "tapping" }] },
                { "type": "function_call", "call_id": "call_9", "name": "tap", "arguments": "{\"role\":\"button\",\"name\":\"Pay\"}" }
              ],
              "usage": { "input_tokens": 11, "output_tokens": 3 }
            }
            """);
        using var http = new HttpClient(handler);
        using var model = new OpenAiResponsesModel(new OpenAiResponsesModelOptions { Model = "gpt-test", ApiKey = "sk" }, http);

        var response = await model.CompleteAsync(Conversation(), CancellationToken.None);

        Assert.Equal("tap", response.ToolCalls[0].Name);
        Assert.Equal("call_9", response.ToolCalls[0].Id);
        Assert.Equal("Pay", response.ToolCalls[0].Arguments.GetProperty("name").GetString());
        Assert.Equal("tapping", response.Content);
        Assert.Equal(11, response.Usage!.InputTokens);

        var request = handler.Requests.Single();
        Assert.Equal("https://api.openai.com/v1/responses", request.Uri.AbsoluteUri);
        Assert.Equal("Bearer sk", request.Headers["Authorization"]);
        var body = request.Json();
        Assert.Equal("rules", body["instructions"]!.GetValue<string>());
        Assert.False(body["store"]!.GetValue<bool>());
        Assert.StartsWith("e2e-", body["prompt_cache_key"]!.GetValue<string>(), StringComparison.Ordinal);
        var input = body["input"]!.AsArray();
        Assert.Equal("input_text", input[0]!["content"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("function_call", input[1]!["type"]!.GetValue<string>());
        Assert.Null(input[1]!["id"]);
        Assert.Equal("call_1", input[1]!["call_id"]!.GetValue<string>());
        Assert.Equal("function_call_output", input[2]!["type"]!.GetValue<string>());
        Assert.Equal("tapped", input[2]!["output"]!.GetValue<string>());
        Assert.Equal("done", body["tools"]![0]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Responses_model_folds_an_event_stream_with_streamed_items()
    {
        var stream = string.Join("\n\n",
            "event: response.created\ndata: {\"type\":\"response.created\",\"response\":{\"output\":[]}}",
            "event: response.output_item.done\ndata: {\"type\":\"response.output_item.done\",\"output_index\":0,\"item\":{\"type\":\"function_call\",\"call_id\":\"c1\",\"name\":\"done\",\"arguments\":\"{\\\"status\\\":\\\"passed\\\",\\\"summary\\\":\\\"ok\\\"}\"}}",
            "event: response.completed\ndata: {\"type\":\"response.completed\",\"response\":{\"output\":[],\"usage\":{\"input_tokens\":2,\"output_tokens\":1}}}",
            "data: [DONE]") + "\n\n";
        var handler = RecordingHandler.WithContentType(stream, "text/event-stream");
        using var http = new HttpClient(handler);
        using var model = new OpenAiResponsesModel(new OpenAiResponsesModelOptions { Model = "m", ApiKey = "k" }, http);

        var response = await model.CompleteAsync(Conversation(), CancellationToken.None);

        Assert.Equal("done", response.ToolCalls.Single().Name);
        Assert.Equal(2, response.Usage!.InputTokens);
    }

    [Fact]
    public async Task Responses_model_reports_a_failed_stream()
    {
        var stream = "data: {\"type\":\"response.failed\",\"response\":{\"error\":{\"message\":\"overloaded\"}}}\n\n";
        using var http = new HttpClient(RecordingHandler.WithContentType(stream, "text/event-stream"));
        using var model = new OpenAiResponsesModel(new OpenAiResponsesModelOptions { Model = "m", ApiKey = "k" }, http);

        var error = await Assert.ThrowsAsync<AgentException>(() => model.CompleteAsync(Conversation(), CancellationToken.None));

        Assert.Equal("MODEL_PROVIDER_FAILED", error.Code);
        Assert.Contains("overloaded", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Azure_preset_sends_the_api_key_header_and_version()
    {
        var handler = new RecordingHandler("""{ "output": [] }""");
        using var http = new HttpClient(handler);
        var preset = ModelProviders.AzureOpenAiAt("my-deployment", "https://res.openai.azure.com/openai/v1", "az-key");
        using var model = new OpenAiResponsesModel(
            new OpenAiResponsesModelOptions
            {
                Model = "my-deployment",
                BaseUrl = "https://res.openai.azure.com/openai/v1",
                Provider = "azure",
                ApiKey = "az-key",
                ApiKeyHeader = "api-key",
                QueryParameters = new Dictionary<string, string> { ["api-version"] = "v1" },
            },
            http);
        preset.Dispose();

        await model.CompleteAsync(Conversation(), CancellationToken.None);

        var request = handler.Requests.Single();
        Assert.Equal("https://res.openai.azure.com/openai/v1/responses?api-version=v1", request.Uri.AbsoluteUri);
        Assert.Equal("az-key", request.Headers["api-key"]);
        Assert.False(request.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task Anthropic_model_marks_cache_breakpoints_and_merges_tool_results()
    {
        var handler = new RecordingHandler("""
            {
              "content": [
                { "type": "text", "text": "ok" },
                { "type": "tool_use", "id": "toolu_1", "name": "done", "input": { "status": "passed", "summary": "ok" } }
              ],
              "usage": { "input_tokens": 5, "cache_read_input_tokens": 100, "output_tokens": 7 }
            }
            """);
        using var http = new HttpClient(handler);
        using var model = new AnthropicModel(new AnthropicModelOptions { Model = "claude-test", ApiKey = "ant" }, http);

        var request = Conversation();
        request = new ModelRequest
        {
            System = request.System,
            Tools = request.Tools,
            Messages = [.. request.Messages, new ModelMessage { Role = "user", Content = "Call done." }],
        };
        var response = await model.CompleteAsync(request, CancellationToken.None);

        Assert.Equal("toolu_1", response.ToolCalls.Single().Id);
        Assert.Equal(105, response.Usage!.InputTokens);
        var sent = handler.Requests.Single();
        Assert.Equal("https://api.anthropic.com/v1/messages", sent.Uri.AbsoluteUri);
        Assert.Equal("ant", sent.Headers["x-api-key"]);
        Assert.Equal("2023-06-01", sent.Headers["anthropic-version"]);
        var body = sent.Json();
        Assert.Equal("ephemeral", body["system"]![0]!["cache_control"]!["type"]!.GetValue<string>());
        var messages = body["messages"]!.AsArray();
        Assert.Equal(3, messages.Count);
        Assert.Equal("tool_use", messages[1]!["content"]![0]!["type"]!.GetValue<string>());
        var last = messages[2]!["content"]!.AsArray();
        Assert.Equal("tool_result", last[0]!["type"]!.GetValue<string>());
        Assert.Equal("text", last[1]!["type"]!.GetValue<string>());
        Assert.Null(last[0]!["cache_control"]);
        Assert.Equal("ephemeral", last[1]!["cache_control"]!["type"]!.GetValue<string>());
        Assert.Equal("done", body["tools"]![0]!["name"]!.GetValue<string>());
        Assert.NotNull(body["tools"]![0]!["input_schema"]);
    }

    [Fact]
    public async Task Google_model_sends_json_schemas_and_returns_thought_signatures()
    {
        var handler = new RecordingHandler(
            """
            {
              "candidates": [{ "content": { "parts": [
                { "functionCall": { "name": "tap", "args": { "role": "button", "name": "Pay" } }, "thoughtSignature": "c2ln" }
              ] } }],
              "usageMetadata": { "promptTokenCount": 9, "candidatesTokenCount": 2, "thoughtsTokenCount": 4 }
            }
            """,
            """{ "candidates": [{ "content": { "parts": [{ "text": "ok" }] } }] }""");
        using var http = new HttpClient(handler);
        using var model = new GoogleModel(new GoogleModelOptions { Model = "gemini-3-pro", ApiKey = "g" }, http);

        var first = await model.CompleteAsync(new ModelRequest { System = "rules", Messages = [new ModelMessage { Role = "user", Content = "go" }], Tools = Tools() }, CancellationToken.None);
        var call = first.ToolCalls.Single();
        Assert.Equal(6, first.Usage!.OutputTokens);
        await model.CompleteAsync(
            new ModelRequest
            {
                System = "rules",
                Tools = Tools(),
                Messages =
                [
                    new ModelMessage { Role = "user", Content = "go" },
                    new ModelMessage { Role = "assistant", ToolCalls = [call] },
                    new ModelMessage { Role = "tool", ToolCallId = call.Id, Content = "tapped" },
                ],
            },
            CancellationToken.None);

        var sent = handler.Requests[0];
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/models/gemini-3-pro:generateContent", sent.Uri.AbsoluteUri);
        Assert.Equal("g", sent.Headers["x-goog-api-key"]);
        Assert.NotNull(sent.Json()["tools"]![0]!["functionDeclarations"]![0]!["parametersJsonSchema"]);
        var contents = handler.Requests[1].Json()["contents"]!.AsArray();
        Assert.Equal("model", contents[1]!["role"]!.GetValue<string>());
        Assert.Equal("c2ln", contents[1]!["parts"]![0]!["thoughtSignature"]!.GetValue<string>());
        Assert.Equal("tap", contents[2]!["parts"]![0]!["functionResponse"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public async Task Bedrock_model_sends_a_bedrock_api_key_as_bearer()
    {
        var handler = new RecordingHandler("""
            { "output": { "message": { "role": "assistant", "content": [
              { "toolUse": { "toolUseId": "t1", "name": "done", "input": { "status": "passed", "summary": "ok" } } }
            ] } }, "usage": { "inputTokens": 3, "outputTokens": 4 } }
            """);
        using var http = new HttpClient(handler);
        using var model = new BedrockModel(new BedrockModelOptions { Model = "us.anthropic.claude-test-v1:0", Region = "eu-west-1", ApiKey = "bedrock-key" }, http);

        var response = await model.CompleteAsync(Conversation(), CancellationToken.None);

        Assert.Equal("t1", response.ToolCalls.Single().Id);
        var sent = handler.Requests.Single();
        Assert.Equal("https://bedrock-runtime.eu-west-1.amazonaws.com/model/us.anthropic.claude-test-v1%3A0/converse", sent.Uri.AbsoluteUri);
        Assert.Equal("Bearer bedrock-key", sent.Headers["Authorization"]);
        var body = sent.Json();
        Assert.Equal("rules", body["system"]![0]!["text"]!.GetValue<string>());
        Assert.Equal("done", body["toolConfig"]!["tools"]![0]!["toolSpec"]!["name"]!.GetValue<string>());
        Assert.Equal("t1", response.ToolCalls[0].Id);
    }

    [Fact]
    public async Task Bedrock_model_signs_with_aws_credentials()
    {
        var handler = new RecordingHandler("""{ "output": { "message": { "content": [{ "text": "ok" }] } } }""");
        using var http = new HttpClient(handler);
        using var model = new BedrockModel(
            new BedrockModelOptions
            {
                Model = "amazon.nova-pro-v1:0",
                Region = "us-west-2",
                ApiKeyEnv = "E2E_TEST_UNSET_BEDROCK_KEY",
                Credentials = new AwsCredentials("AKIDEXAMPLE", "secret", "session"),
            },
            http);

        await model.CompleteAsync(Conversation(), CancellationToken.None);

        var sent = handler.Requests.Single();
        Assert.StartsWith("AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/", sent.Headers["Authorization"], StringComparison.Ordinal);
        Assert.Contains("/us-west-2/bedrock/aws4_request, SignedHeaders=content-type;host;x-amz-content-sha256;x-amz-date;x-amz-security-token, Signature=", sent.Headers["Authorization"], StringComparison.Ordinal);
        Assert.Equal("session", sent.Headers["x-amz-security-token"]);
        Assert.Equal("application/json", sent.ContentType);
    }

    [Fact]
    public void SigV4_matches_the_aws_documentation_example()
    {
        // The IAM ListUsers example from the AWS Signature Version 4 documentation.
        const string canonical = "GET\n/\nAction=ListUsers&Version=2010-05-08\ncontent-type:application/x-www-form-urlencoded; charset=utf-8\nhost:iam.amazonaws.com\nx-amz-date:20150830T123600Z\n\ncontent-type;host;x-amz-date\ne3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
        Assert.Equal("f536975d06c0309214f805bb90ccff089219ecd68b2577efef23edd43b7e1a59", SigV4.Hash(canonical));
        var toSign = "AWS4-HMAC-SHA256\n20150830T123600Z\n20150830/us-east-1/iam/aws4_request\nf536975d06c0309214f805bb90ccff089219ecd68b2577efef23edd43b7e1a59";
        Assert.Equal(
            "5d672d79c15b13162d9279b0855cfba6789a8edb4c82c400e06b5924a6f2b5d7",
            SigV4.Signature("wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY", "20150830", "us-east-1", "iam", toSign));
        Assert.Equal("/model/a%253Ab/converse", SigV4.CanonicalPath("/model/a%3Ab/converse"));
    }

    [Fact]
    public async Task OpenAi_chat_requests_carry_cache_hints_only_for_openai()
    {
        var openai = new RecordingHandler("""{ "choices": [{ "message": { "content": "ok" } }] }""");
        using (var http = new HttpClient(openai))
        using (var model = new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions { Model = "gpt", ApiKey = "k" }, http))
        {
            await model.CompleteAsync(Conversation(), CancellationToken.None);
        }

        var local = new RecordingHandler("""{ "choices": [{ "message": { "content": "ok" } }] }""");
        using (var http = new HttpClient(local))
        using (var model = new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions { Model = "llama", BaseUrl = "http://127.0.0.1:11434/v1" }, http))
        {
            await model.CompleteAsync(Conversation(), CancellationToken.None);
        }

        Assert.False(openai.Requests.Single().Json()["store"]!.GetValue<bool>());
        Assert.NotNull(openai.Requests.Single().Json()["prompt_cache_key"]);
        Assert.Null(local.Requests.Single().Json()["store"]);
        Assert.False(local.Requests.Single().Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public async Task OpenRouter_preset_sends_attribution_headers()
    {
        var handler = new RecordingHandler("""{ "choices": [{ "message": { "content": "ok" } }] }""");
        using var http = new HttpClient(handler);
        using var model = new OpenAiCompatibleModel(
            new OpenAiCompatibleModelOptions
            {
                Model = "openai/gpt-6-luna-fast",
                BaseUrl = "https://openrouter.ai/api/v1",
                ApiKey = "or",
                Headers = ModelProviders.AttributionHeaders,
            },
            http);

        await model.CompleteAsync(Conversation(), CancellationToken.None);

        var sent = handler.Requests.Single();
        Assert.Equal("https://openrouter.ai/api/v1/chat/completions", sent.Uri.AbsoluteUri);
        Assert.Equal("e2e-dotnet", sent.Headers["X-Title"]);
    }

    [Fact]
    public void Gateway_falls_back_to_the_vercel_oidc_token()
    {
        var options = new OpenAiCompatibleModelOptions
        {
            Model = "m",
            ApiKeyEnv = "E2E_TEST_UNSET_GATEWAY_KEY",
            ApiKeyFallbackEnv = "PATH",
        };

        Assert.Equal(Environment.GetEnvironmentVariable("PATH"), options.ResolveApiKey());
    }

    [Fact]
    public void Config_reads_the_provider_and_creates_its_client()
    {
        var root = Path.Combine(Path.GetTempPath(), "e2e-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "e2e.config.json"), """
                {
                  "agents": {
                    "default": { "model": "claude-test", "provider": "anthropic" },
                    "gemini": { "model": "gemini-test", "provider": "google", "apiKeyEnv": "MY_GEMINI_KEY" },
                    "bedrock": { "model": "amazon.nova-pro-v1:0", "provider": "bedrock" },
                    "router": { "model": "openai/gpt", "provider": "openrouter" },
                    "copilot": { "model": "gpt-5", "provider": "copilot" },
                    "chatgpt": { "model": "gpt-6-luna", "provider": "chatgpt" }
                  }
                }
                """);
            var config = E2EConfig.Load(Path.Combine(root, "e2e.config.json"));

            Assert.Equal("anthropic", config.Agent.Provider);
            Assert.IsType<AnthropicModel>(config.Agent.CreateModel());
            Assert.IsType<GoogleModel>(config.Agents["gemini"].CreateModel());
            Assert.Equal("MY_GEMINI_KEY", config.Agents["gemini"].ApiKeyEnv);
            Assert.IsType<BedrockModel>(config.Agents["bedrock"].CreateModel());
            Assert.IsType<OpenAiCompatibleModel>(config.Agents["router"].CreateModel());
            Assert.IsType<OAuth.CopilotModel>(config.Agents["copilot"].CreateModel());
            Assert.IsType<OpenAiResponsesModel>(config.Agents["chatgpt"].CreateModel());
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("""{ "agents": { "default": { "model": "m", "provider": "cohere" } } }""", "agents.default.provider must be one of")]
    [InlineData("""{ "agents": { "default": { "model": "m", "provider": "copilot", "apiKeyEnv": "X" } } }""", "baseUrl and apiKeyEnv do not apply")]
    [InlineData("""{ "agents": { "default": { "model": "m", "provider": "openai-compatible" } } }""", "baseUrl is required")]
    public void Config_refuses_a_bad_provider(string json, string message)
    {
        var root = Path.Combine(Path.GetTempPath(), "e2e-provider-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "e2e.config.json"), json);
            var error = Assert.Throws<ConfigurationException>(() => E2EConfig.Load(Path.Combine(root, "e2e.config.json")));
            Assert.Equal("INVALID_CONFIG", error.Code);
            Assert.Contains(message, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    internal static ModelRequest Conversation()
    {
        var call = new ModelToolCall { Id = "call_1", Name = "tap", Arguments = JsonSerializer.SerializeToElement(new { role = "button", name = "Pay" }) };
        return new ModelRequest
        {
            System = "rules",
            Tools = Tools(),
            Messages =
            [
                new ModelMessage { Role = "user", Content = "Goal: pay" },
                new ModelMessage { Role = "assistant", ToolCalls = [call] },
                new ModelMessage { Role = "tool", ToolCallId = "call_1", Name = "tap", Content = "tapped" },
            ],
        };
    }

    internal static IReadOnlyList<ModelTool> Tools()
    {
        return
        [
            new ModelTool
            {
                Name = "done",
                Description = "finish",
                Parameters = JsonSerializer.SerializeToElement(new { type = "object", properties = new { status = new { type = "string" } } }),
            },
        ];
    }
}

/// <summary>Answers each request with the next queued body and records what was sent.</summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string Body, string ContentType)> _answers = new();

    public RecordingHandler(params string[] bodies)
    {
        foreach (var body in bodies)
        {
            _answers.Enqueue((HttpStatusCode.OK, body, "application/json"));
        }
    }

    public static RecordingHandler WithContentType(string body, string contentType)
    {
        var handler = new RecordingHandler();
        handler._answers.Enqueue((HttpStatusCode.OK, body, contentType));
        return handler;
    }

    public List<RecordedRequest> Requests { get; } = [];

    public RecordingHandler Then(HttpStatusCode status, string body)
    {
        _answers.Enqueue((status, body, "application/json"));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(",", header.Value);
        }

        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, headers, body, request.Content?.Headers.ContentType?.ToString()));
        var (status, answer, contentType) = _answers.Count > 1 ? _answers.Dequeue() : _answers.Peek();
        return new HttpResponseMessage(status) { Content = new StringContent(answer, Encoding.UTF8, contentType) };
    }
}

internal sealed record RecordedRequest(HttpMethod Method, Uri Uri, Dictionary<string, string> Headers, string Body, string? ContentType)
{
    public JsonObject Json() => JsonNode.Parse(Body)!.AsObject();
}
