// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace E2E.Mobile.Tests;

/// <summary>One call the fake recorded: the <c>namespace.method</c> and its options object.</summary>
internal sealed record RecordedCall(string Method, JsonObject? Args);

/// <summary>
/// A scripted agent-device client: every call is recorded as <c>namespace.method</c> with its options and answered by
/// the responder registered under that name (or an empty object). Tests assert on the command stream the engine
/// produced, which is the whole of what the engine owes agent-device.
/// </summary>
internal sealed class FakeDeviceClient : IDeviceClient
{
    private static readonly JsonSerializerOptions CamelCase = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    private readonly Dictionary<string, Func<JsonObject?, CancellationToken, Task<JsonNode?>>> _responders = [];

    /// <summary>An iOS Settings-like screen: app root, navigation bar, a cell, a switch, two fields, a hidden button.</summary>
    public static JsonObject SettingsSnapshot() => new()
    {
        ["nodes"] = JsonSerializer.SerializeToNode(RawNodes.SettingsNodes, CamelCase),
        ["appName"] = "Settings",
        ["appBundleId"] = "com.apple.Preferences",
        ["truncated"] = false,
    };

    public List<RecordedCall> Calls { get; } = [];

    /// <summary>Registers or replaces the responder for one <c>namespace.method</c>.</summary>
    public void Respond(string method, Func<JsonObject?, JsonNode?> responder) =>
        _responders[method] = (options, _) => Task.FromResult(responder(options));

    /// <summary>Registers an asynchronous responder, for a call that waits or never answers.</summary>
    public void RespondAsync(string method, Func<JsonObject?, CancellationToken, Task<JsonNode?>> responder) => _responders[method] = responder;

    /// <summary>Methods called so far, in order.</summary>
    public List<string> Methods() => [.. Calls.Select(call => call.Method)];

    /// <summary>The recorded argument of the last call to one method.</summary>
    public JsonObject? LastArgs(string method) => Calls.FindLast(call => call.Method == method)?.Args;

    public async Task<JsonNode?> CallAsync(string method, JsonObject? options, CancellationToken cancellationToken)
    {
        Calls.Add(new RecordedCall(method, (JsonObject?)options?.DeepClone()));
        await Task.Yield();
        return _responders.TryGetValue(method, out var responder) ? await responder(options, cancellationToken).ConfigureAwait(false) : new JsonObject();
    }
}

internal static class JsonAssert
{
    /// <summary>Asserts a recorded options object equals the JSON text, whatever the order of its keys.</summary>
    public static void Args(string expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), $"expected {expected} but was {actual?.ToJsonString() ?? "null"}");
}
