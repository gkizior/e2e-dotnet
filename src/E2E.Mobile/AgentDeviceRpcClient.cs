// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace E2E.Mobile;

/// <summary>Where one agent-device daemon answers: its base URL and the bearer token it expects, when it wants one.</summary>
public sealed record DaemonEndpoint(Uri BaseUrl, string? Token);

/// <summary>
/// The client for an agent-device daemon over HTTP JSON-RPC 2.0: <c>POST /rpc</c> with method
/// <c>agent_device.command</c> and params <c>{ session, command, positionals, flags, meta }</c>, answered by
/// <c>{ ok, data }</c> or by a JSON-RPC error whose <c>data</c> carries the daemon's code, hint, and details.
/// The protocol is not a documented public one: it was read from agent-device's code and checked against a running
/// 0.21.22 daemon (<c>GET /health</c>, <c>devices</c>, <c>boot</c>, <c>close</c>, <c>session_list</c>, <c>prepare</c>,
/// <c>snapshot</c>). It can change between versions, so <see cref="AgentDeviceVersion.Pinned"/> is exact.
/// </summary>
public sealed class AgentDeviceRpcClient : IDeviceClient
{
    private static readonly string[] LeaseMetaKeys = ["tenant", "runId", "leaseId", "sessionIsolation"];

    private readonly string _session;
    private readonly JsonObject _settings;
    private readonly Func<CancellationToken, Task<DaemonEndpoint>> _endpoint;
    private readonly HttpClient _http;
    private int _requests;

    /// <param name="session">The agent-device session every command runs under.</param>
    /// <param name="settings">The client configuration of a lease (<see cref="DeviceConnection.Client"/>): flags every command carries.</param>
    /// <param name="endpoint">Resolves the daemon, once, on the first call: a local daemon may need starting.</param>
    /// <param name="http">The transport. Its timeout must be infinite: the daemon and the caller's token bound each command.</param>
    public AgentDeviceRpcClient(string session, JsonObject? settings, Func<CancellationToken, Task<DaemonEndpoint>> endpoint, HttpClient http)
    {
        _session = session;
        _settings = settings is null ? new JsonObject() : (JsonObject)settings.DeepClone();
        _settings.Remove("stateDir");
        _endpoint = endpoint;
        _http = http;
    }

    public async Task<JsonNode?> CallAsync(string method, JsonObject? options, CancellationToken cancellationToken)
    {
        var command = DeviceCommands.Translate(method, options);
        var endpoint = await _endpoint(cancellationToken).ConfigureAwait(false);
        var flags = (JsonObject)_settings.DeepClone();
        foreach (var (key, value) in command.Flags)
        {
            flags[key] = value?.DeepClone();
        }

        var requestId = $"e2e-{Guid.NewGuid():N}"[..20];
        var meta = new JsonObject { ["requestId"] = requestId };
        foreach (var key in LeaseMetaKeys)
        {
            if (_settings[key] is { } value)
            {
                meta[key == "tenant" ? "tenantId" : key] = value.DeepClone();
            }
        }

        var request = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Interlocked.Increment(ref _requests),
            ["method"] = "agent_device.command",
            ["params"] = new JsonObject
            {
                ["session"] = _session,
                ["command"] = command.Name,
                ["positionals"] = new JsonArray([.. command.Positionals.Select(text => (JsonNode?)JsonValue.Create(text))]),
                ["flags"] = flags,
                ["meta"] = meta,
            },
        };
        using var message = new HttpRequestMessage(HttpMethod.Post, new Uri(endpoint.BaseUrl.AbsoluteUri.TrimEnd('/') + "/rpc"))
        {
            Content = new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(endpoint.Token))
        {
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", endpoint.Token);
        }

        JsonNode? response;
        try
        {
            using var reply = await _http.SendAsync(message, cancellationToken).ConfigureAwait(false);
            var text = await reply.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            response = JsonNode.Parse(text);
        }
        catch (Exception cause) when (cause is HttpRequestException or JsonException)
        {
            throw new AgentDeviceError("COMMAND_FAILED", $"Daemon request failed: {cause.Message}", inner: cause);
        }

        if (response?["error"] is JsonObject error)
        {
            throw ToError(error["data"] as JsonObject, error["message"]?.GetValue<string>());
        }

        var result = response?["result"] as JsonObject
            ?? throw new AgentDeviceError("COMMAND_FAILED", "Daemon returned a response with neither a result nor an error.");
        if (result["ok"] is JsonValue ok && ok.TryGetValue<bool>(out var succeeded) && !succeeded)
        {
            throw ToError(result["error"] as JsonObject, null);
        }

        var data = result["data"];
        return method switch
        {
            DeviceMethods.DevicesList => (data as JsonObject)?["devices"] as JsonArray ?? [],
            DeviceMethods.SessionsList => (data as JsonObject)?["sessions"] as JsonArray ?? [],
            _ => data ?? new JsonObject(),
        };
    }

    /// <summary>
    /// The daemon's error as agent-device's client raises it: its code and message, and the details it declared with
    /// the hint, diagnostic id, and log path folded in.
    /// </summary>
    private static AgentDeviceError ToError(JsonObject? data, string? fallbackMessage)
    {
        var code = Text(data?["code"]) ?? "COMMAND_FAILED";
        var message = Text(data?["message"]) ?? fallbackMessage ?? "The daemon reported a failure.";
        var details = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (data?["details"] is JsonObject declared)
        {
            foreach (var (key, value) in declared)
            {
                details[key] = Plain(value);
            }
        }

        foreach (var key in new[] { "hint", "diagnosticId", "logPath", "retriable", "supportedOn" })
        {
            if (data?[key] is { } value)
            {
                details[key] = Plain(value);
            }
        }

        return new AgentDeviceError(code, message, details);
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>A JSON value as the plain object a details bag holds: strings, numbers, booleans stay, anything nested stays a node.</summary>
    private static object? Plain(JsonNode? node) => node switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        JsonValue value when value.TryGetValue<bool>(out var flag) => flag,
        JsonValue value when value.TryGetValue<double>(out var number) => number,
        _ => node,
    };
}
