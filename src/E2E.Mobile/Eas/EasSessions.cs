// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace E2E.Mobile.Eas;

/// <summary>What the provider asks EAS for: one agent-device session on one platform.</summary>
internal sealed record EasSessionParams
{
    public required string AppId { get; init; }

    public required MobilePlatform Platform { get; init; }

    public required string Name { get; init; }

    public required IReadOnlyList<string> Tags { get; init; }

    public string? BuildId { get; init; }

    public string? ApplicationArchiveUrl { get; init; }

    /// <summary>An iOS device name or UDID, or an Android AVD hardware profile.</summary>
    public string? DeviceIdentifier { get; init; }

    public int? MaxIdleTimeMinutes { get; init; }

    public int? MaxRunTimeMinutes { get; init; }

    /// <summary>The agent-device version EAS starts the daemon at.</summary>
    public string? PackageVersion { get; init; }
}

/// <summary>Where a session is: queued until the account has a free simulator, starting while it boots, ready once its daemon is reachable.</summary>
internal enum EasPhase
{
    Queued,
    Starting,
    Ready,
    Ended,
}

internal sealed record EasSessionState(
    EasPhase Phase,
    string? DaemonUrl = null,
    string? DaemonToken = null,
    string? OpenPreviewUrl = null,
    string? EndedStatus = null);

internal sealed record EasCreatedSession(string Id, string Url);

/// <summary>An error EAS answered with, its <c>errorCode</c> kept.</summary>
internal sealed class EasException(string message, string? code) : Exception($"EAS: {message}")
{
    public string? Code { get; } = code;
}

/// <summary>
/// EAS Simulators' sessions through Expo's GraphQL API over <see cref="HttpClient"/>, the API <c>eas simulator:*</c> uses,
/// so the integration installs no SDK.
/// </summary>
internal sealed class EasSessions(ExpoCredentials credentials, HttpClient http)
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(60);

    private static readonly Uri GraphqlUrl = new("https://api.expo.dev/graphql");

    private const string Create = """
        mutation ($input: CreateDeviceRunSessionInput!) {
          deviceRunSession { createDeviceRunSession(deviceRunSessionInput: $input) { id app { slug ownerAccount { name } } } }
        }
        """;

    private const string State = """
        query ($id: ID!) {
          deviceRunSessions { byId(deviceRunSessionId: $id) {
            status
            turtleJobRun { status }
            remoteConfig { ... on AgentDeviceRunSessionRemoteConfig { agentDeviceRemoteSessionUrl agentDeviceRemoteSessionToken webPreviewUrl webPreviewToken } }
          } }
        }
        """;

    private const string Stop = """
        mutation ($id: ID!) {
          deviceRunSession { ensureDeviceRunSessionStopped(deviceRunSessionId: $id) { turtleJobRun { id status } } }
        }
        """;

    private const string CancelJob = """
        mutation ($id: ID!) {
          jobRun { cancelJobRun(jobRunId: $id) { id } }
        }
        """;

    /// <summary>Job run states before the job picks the session up: waiting for the account's concurrency.</summary>
    private static readonly HashSet<string> QueuedJob = ["NEW", "IN_QUEUE"];

    /// <summary>Job run states after which the session never becomes ready.</summary>
    private static readonly HashSet<string> EndedJob = ["ERRORED", "CANCELED", "FINISHED", "PENDING_CANCEL"];

    public async Task<EasCreatedSession> CreateAsync(EasSessionParams session, CancellationToken cancellationToken)
    {
        var input = new JsonObject
        {
            ["appId"] = session.AppId,
            ["name"] = session.Name,
            ["tags"] = new JsonArray([.. session.Tags.Select(tag => (JsonNode?)JsonValue.Create(tag))]),
            ["platform"] = session.Platform.WireName().ToUpperInvariant(),
            ["type"] = "AGENT_DEVICE",
        };
        Set(input, "buildId", session.BuildId);
        Set(input, "applicationArchiveUrl", session.ApplicationArchiveUrl);
        Set(input, "maxIdleTimeMinutes", session.MaxIdleTimeMinutes);
        Set(input, "maxRunTimeMinutes", session.MaxRunTimeMinutes);
        Set(input, "packageVersion", session.PackageVersion);
        if (session.DeviceIdentifier is not null)
        {
            input[session.Platform.WireName()] = new JsonObject { ["deviceIdentifier"] = session.DeviceIdentifier };
        }

        var data = await GraphqlAsync(Create, new JsonObject { ["input"] = input }, cancellationToken).ConfigureAwait(false);
        var created = data["deviceRunSession"]?["createDeviceRunSession"];
        var id = created?["id"]?.GetValue<string>() ?? throw new InvalidOperationException("EAS: the created session has no id");
        var app = created["app"];
        var account = Uri.EscapeDataString(app?["ownerAccount"]?["name"]?.GetValue<string>() ?? "");
        var slug = Uri.EscapeDataString(app?["slug"]?.GetValue<string>() ?? "");
        return new EasCreatedSession(id, $"https://expo.dev/accounts/{account}/projects/{slug}/simulator-sessions/{id}");
    }

    public async Task<EasSessionState> StateAsync(string id, CancellationToken cancellationToken)
    {
        var data = await GraphqlAsync(State, new JsonObject { ["id"] = id }, cancellationToken).ConfigureAwait(false);
        var session = data["deviceRunSessions"]?["byId"] ?? throw new InvalidOperationException($"EAS: session {id} not found");
        var status = session["status"]?.GetValue<string>();
        var job = session["turtleJobRun"]?["status"]?.GetValue<string>();
        var config = session["remoteConfig"];
        if (status is "STOPPED" or "ERRORED")
        {
            return new EasSessionState(EasPhase.Ended, EndedStatus: status.ToLowerInvariant());
        }

        // A job that ended keeps the daemon address it had, which no longer answers.
        if (job is not null && EndedJob.Contains(job))
        {
            return new EasSessionState(EasPhase.Ended, EndedStatus: $"job {job.ToLowerInvariant()}");
        }

        if (config?["agentDeviceRemoteSessionUrl"]?.GetValue<string>() is { } url && config["agentDeviceRemoteSessionToken"]?.GetValue<string>() is { } token)
        {
            var preview = config["webPreviewToken"] is null ? config["webPreviewUrl"]?.GetValue<string>() : null;
            return new EasSessionState(EasPhase.Ready, url, token, preview);
        }

        return new EasSessionState(job is null || QueuedJob.Contains(job) ? EasPhase.Queued : EasPhase.Starting);
    }

    /// <summary>
    /// Stops the session, and cancels its job when it is still queued: a stopped session's queued job still starts later
    /// and holds one of the account's concurrent sessions while it does. Stopping one already stopped, or one EAS no
    /// longer knows, succeeds.
    /// </summary>
    public async Task StopAsync(string id, CancellationToken cancellationToken)
    {
        JsonNode? job;
        try
        {
            var data = await GraphqlAsync(Stop, new JsonObject { ["id"] = id }, cancellationToken).ConfigureAwait(false);
            job = data["deviceRunSession"]?["ensureDeviceRunSessionStopped"]?["turtleJobRun"];
        }
        catch (EasException cause) when (cause.Code == "NOT_FOUND_ERROR")
        {
            return;
        }

        if (job is not null && job["status"]?.GetValue<string>() is { } status && QueuedJob.Contains(status))
        {
            await GraphqlAsync(CancelJob, new JsonObject { ["id"] = job["id"]?.GetValue<string>() }, cancellationToken).ConfigureAwait(false);
        }
    }

    private static void Set(JsonObject target, string name, string? value)
    {
        if (value is not null)
        {
            target[name] = value;
        }
    }

    private static void Set(JsonObject target, string name, int? value)
    {
        if (value is { } number)
        {
            target[name] = number;
        }
    }

    /// <summary>Runs one operation; a GraphQL error throws with EAS's message and error code.</summary>
    private async Task<JsonNode> GraphqlAsync(string query, JsonObject variables, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, GraphqlUrl)
        {
            Content = new StringContent(new JsonObject { ["query"] = query, ["variables"] = variables }.ToJsonString(), Encoding.UTF8, new MediaTypeHeaderValue("application/json")),
        };
        var (name, value) = credentials.Header;
        request.Headers.TryAddWithoutValidation(name, value);

        // A connection that stalls must not hang a poll, or the startup limits never run.
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(RequestTimeout);
        using var response = await http.SendAsync(request, limit.Token).ConfigureAwait(false);
        var text = await response.Content.ReadAsStringAsync(limit.Token).ConfigureAwait(false);
        var status = ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture);
        JsonNode? body;
        try
        {
            body = JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            body = null;
        }

        if (body is not JsonObject root)
        {
            var excerpt = text.Trim();
            throw new InvalidOperationException($"EAS: HTTP {status}, {(excerpt.Length > 200 ? excerpt[..200] : excerpt)}");
        }

        if (root["errors"] is JsonArray { Count: > 0 } errors && errors[0] is { } error)
        {
            throw new EasException(error["message"]?.GetValue<string>() ?? $"HTTP {status}", error["extensions"]?["errorCode"]?.GetValue<string>());
        }

        return !response.IsSuccessStatusCode || root["data"] is not { } data ? throw new InvalidOperationException($"EAS: HTTP {status}, no data") : data;
    }
}
