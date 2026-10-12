// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;

namespace E2E.Tests.ProviderHints;

/// <summary>
/// Sends a request through a client and reads the body it put on the wire, as upstream's <c>hintedBody</c> reads the
/// body a provider builds. The port has no <c>providerHints</c> function: each client adds its own hints to its body.
/// </summary>
internal static class HintedBodies
{
    public const string ChatAnswer = """{ "choices": [{ "message": { "content": "ok" } }] }""";

    public const string ResponsesAnswer = """{ "output": [{ "type": "message", "content": [{ "type": "output_text", "text": "ok" }] }] }""";

    public const string AnthropicAnswer = """{ "content": [{ "type": "text", "text": "ok" }] }""";

    public static ModelRequest Request(string system = "rules", IReadOnlyDictionary<string, JsonElement>? providerOptions = null, params ModelMessage[] messages) => new()
    {
        System = system,
        Tools = ModelProviderTests.Tools(),
        Messages = messages.Length == 0 ? [new ModelMessage { Role = "user", Content = "step" }] : messages,
        ProviderOptions = providerOptions,
    };

    public static IReadOnlyDictionary<string, JsonElement> Options(string provider, string json) =>
        new Dictionary<string, JsonElement> { [provider] = JsonDocument.Parse(json).RootElement.Clone() };

    public static async Task<JsonObject> SendAsync(IAgentModel model, RecordingHandler handler, ModelRequest request)
    {
        await model.CompleteAsync(request, CancellationToken.None);
        return handler.Requests.Single().Json();
    }

    public static async Task<JsonObject> ChatAsync(OpenAiCompatibleModelOptions options, ModelRequest? request = null)
    {
        var handler = new RecordingHandler(ChatAnswer);
        using var http = new HttpClient(handler);
        using var model = new OpenAiCompatibleModel(options, http);
        return await SendAsync(model, handler, request ?? Request());
    }

    public static async Task<JsonObject> ResponsesAsync(OpenAiResponsesModelOptions options, ModelRequest? request = null)
    {
        var handler = new RecordingHandler(ResponsesAnswer);
        using var http = new HttpClient(handler);
        using var model = new OpenAiResponsesModel(options, http);
        return await SendAsync(model, handler, request ?? Request());
    }
}
