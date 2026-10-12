// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace E2E.Mobile;

/// <summary>
/// The agent-device daemon the engine talks to, and the checks that fail early with a clear error: Node 22.12 or
/// newer on the path, the pinned agent-device, a daemon that speaks the RPC protocol this client was written for.
/// agent-device is a Node program, so a local daemon needs Node at run time. The daemon is started the way
/// agent-device's own CLI starts it, by running one cheap command, and it keeps running after that command exits.
/// </summary>
public static partial class AgentDeviceDaemon
{
    /// <summary>The environment variable that names a local <c>agent-device.mjs</c> to run in place of <c>npx agent-device</c>.</summary>
    public const string BinaryVariable = "E2E_AGENT_DEVICE_BIN";

    private static readonly ConcurrentDictionary<string, Lazy<Task<DaemonEndpoint>>> Local = new(StringComparer.Ordinal);

    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

    /// <summary>The transport every client shares.</summary>
    internal static HttpClient SharedHttp => Http;

    /// <summary>
    /// Where the daemon this process starts keeps its state. A directory of its own keeps it apart from a socket
    /// daemon the same user runs from agent-device's CLI, which cannot answer HTTP.
    /// </summary>
    public static string DefaultStateDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".agent-device", "e2e-dotnet");

    /// <summary>The local daemon for a state directory: found if one runs there, else started. One start per directory per process.</summary>
    public static async Task<DaemonEndpoint> LocalAsync(string? stateDirectory, CancellationToken cancellationToken)
    {
        var directory = stateDirectory ?? DefaultStateDirectory;
        var starting = Local.GetOrAdd(directory, key => new Lazy<Task<DaemonEndpoint>>(() => StartAsync(key)));
        try
        {
            return await starting.Value.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (starting.Value.IsFaulted)
        {
            // A failed start is not kept: the user may fix Node or stop the stale daemon and run again in this process.
            Local.TryRemove(new KeyValuePair<string, Lazy<Task<DaemonEndpoint>>>(directory, starting));
            throw;
        }
    }

    /// <summary>
    /// Asks a daemon who it is and checks it is the version and protocol this client speaks. Throws a
    /// <see cref="ConfigurationException"/> naming the mismatch.
    /// </summary>
    public static async Task CheckHealthAsync(DaemonEndpoint endpoint, HttpClient http, CancellationToken cancellationToken)
    {
        JsonNode? health;
        try
        {
            using var reply = await http.GetAsync(new Uri(endpoint.BaseUrl.AbsoluteUri.TrimEnd('/') + "/health"), cancellationToken).ConfigureAwait(false);
            health = JsonNode.Parse(await reply.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (Exception cause) when (cause is HttpRequestException or JsonException)
        {
            throw new ConfigurationException("INVALID_CONFIG", $"The agent-device daemon at {endpoint.BaseUrl} did not answer GET /health: {cause.Message}", cause);
        }

        var protocol = health?["rpcProtocolVersion"] is JsonValue value && value.TryGetValue<int>(out var number) ? number : (int?)null;
        var ok = health?["ok"] is JsonValue flag && flag.TryGetValue<bool>(out var healthy) && healthy;
        if (!ok || protocol != AgentDeviceVersion.RpcProtocol)
        {
            throw new ConfigurationException(
                "INVALID_CONFIG",
                $"The agent-device daemon at {endpoint.BaseUrl} speaks RPC protocol {protocol?.ToString(CultureInfo.InvariantCulture) ?? "none"}, but this client speaks {AgentDeviceVersion.RpcProtocol}. Use agent-device {AgentDeviceVersion.Pinned}.");
        }
    }

    /// <summary>The Node version a <c>node --version</c> line reports, or null when it names none.</summary>
    public static Version? ParseNodeVersion(string output)
    {
        var match = NodeVersion().Match(output.Trim());
        return match.Success ? new Version(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture)) : null;
    }

    private static async Task<DaemonEndpoint> StartAsync(string stateDirectory)
    {
        Directory.CreateDirectory(stateDirectory);
        if (ReadEndpoint(stateDirectory) is { } running && await IsHealthyAsync(running).ConfigureAwait(false))
        {
            // A daemon of another version speaks another wire format, even when it answers the same protocol.
            CheckVersion(stateDirectory);
            return running;
        }

        await CheckNodeAsync().ConfigureAwait(false);
        var (program, arguments) = Launcher();
        var start = new ProcessStartInfo(program) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (var argument in new[] { "session", "list", "--json" })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["AGENT_DEVICE_STATE_DIR"] = stateDirectory;
        start.Environment["AGENT_DEVICE_DAEMON_TRANSPORT"] = "http";
        start.Environment["AGENT_DEVICE_DAEMON_SERVER_MODE"] = "http";
        var (exitCode, output, error) = await RunAsync(start, TimeSpan.FromMinutes(3)).ConfigureAwait(false);
        if (exitCode != 0)
        {
            throw new ConfigurationException(
                "INVALID_CONFIG",
                $"Could not start the agent-device daemon ({program} {string.Join(' ', arguments)}) with exit code {exitCode}: {FirstLine(error.Length > 0 ? error : output)}. agent-device {AgentDeviceVersion.Pinned} needs Node {AgentDeviceVersion.MinimumNode} or newer; install it with `npm install --global agent-device@{AgentDeviceVersion.Pinned}` or let npx fetch it.");
        }

        var endpoint = ReadEndpoint(stateDirectory)
            ?? throw new ConfigurationException("INVALID_CONFIG", $"The agent-device daemon in {stateDirectory} does not serve HTTP. Stop it with `npx agent-device daemon stop --state-dir {stateDirectory}` and run again.");
        await CheckHealthAsync(endpoint, Http, CancellationToken.None).ConfigureAwait(false);
        CheckVersion(stateDirectory);
        return endpoint;
    }

    /// <summary>Reads the HTTP address and token a running daemon registered in its state directory.</summary>
    private static DaemonEndpoint? ReadEndpoint(string stateDirectory)
    {
        var info = ReadInfo(stateDirectory);
        if (info?["httpPort"] is JsonValue port && port.TryGetValue<int>(out var number))
        {
            return new DaemonEndpoint(new Uri($"http://127.0.0.1:{number.ToString(CultureInfo.InvariantCulture)}/"), Text(info["token"]));
        }

        return null;
    }

    private static JsonNode? ReadInfo(string stateDirectory)
    {
        try
        {
            return JsonNode.Parse(File.ReadAllText(Path.Combine(stateDirectory, "daemon.json")));
        }
        catch (Exception cause) when (cause is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void CheckVersion(string stateDirectory)
    {
        var version = Text(ReadInfo(stateDirectory)?["version"]);
        if (version != AgentDeviceVersion.Pinned)
        {
            throw new ConfigurationException(
                "INVALID_CONFIG",
                $"The agent-device daemon in {stateDirectory} is version {version ?? "unknown"}, but this client was written for {AgentDeviceVersion.Pinned}. Stop it with `npx agent-device daemon stop --state-dir {stateDirectory}` and run again.");
        }
    }

    private static async Task<bool> IsHealthyAsync(DaemonEndpoint endpoint)
    {
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await CheckHealthAsync(endpoint, Http, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (Exception cause) when (cause is ConfigurationException or OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task CheckNodeAsync()
    {
        (int ExitCode, string Output, string Error) result;
        try
        {
            result = await RunAsync(new ProcessStartInfo("node", "--version") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false }, TimeSpan.FromSeconds(30)).ConfigureAwait(false);
        }
        catch (System.ComponentModel.Win32Exception cause)
        {
            throw new ConfigurationException("INVALID_CONFIG", $"Node {AgentDeviceVersion.MinimumNode} or newer is required to run agent-device, and `node` is not on the path. Install it from https://nodejs.org.", cause);
        }

        var version = result.ExitCode == 0 ? ParseNodeVersion(result.Output) : null;
        if (version is null || version < AgentDeviceVersion.MinimumNode)
        {
            throw new ConfigurationException(
                "INVALID_CONFIG",
                $"agent-device needs Node {AgentDeviceVersion.MinimumNode} or newer, and `node --version` reported {FirstLine(result.Output)}. Install a newer Node from https://nodejs.org.");
        }
    }

    /// <summary>The command that runs agent-device: the binary <see cref="BinaryVariable"/> names under node, else the pinned release through npx.</summary>
    private static (string Program, string[] Arguments) Launcher() =>
        Environment.GetEnvironmentVariable(BinaryVariable) is { Length: > 0 } binary
            ? ("node", [binary])
            : (OperatingSystem.IsWindows() ? "npx.cmd" : "npx", ["--yes", $"agent-device@{AgentDeviceVersion.Pinned}"]);

    private static async Task<(int ExitCode, string Output, string Error)> RunAsync(ProcessStartInfo start, TimeSpan timeout)
    {
        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (System.ComponentModel.Win32Exception cause)
        {
            throw new ConfigurationException("INVALID_CONFIG", $"Could not run `{start.FileName}`: {cause.Message}. agent-device needs Node {AgentDeviceVersion.MinimumNode} or newer, and npx comes with it.", cause);
        }

        using var expiry = new CancellationTokenSource(timeout);
        var output = process.StandardOutput.ReadToEndAsync(expiry.Token);
        var error = process.StandardError.ReadToEndAsync(expiry.Token);
        try
        {
            await process.WaitForExitAsync(expiry.Token).ConfigureAwait(false);
            return (process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new ConfigurationException("INVALID_CONFIG", $"`{start.FileName}` did not finish within {timeout.TotalSeconds:0} seconds.");
        }
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "no output";

    [GeneratedRegex(@"^v?(\d+)\.(\d+)")]
    private static partial Regex NodeVersion();
}

/// <summary>What the engine hands to a client: the session, and where and how to reach the daemon.</summary>
public sealed record DeviceClientConfig
{
    public required string Session { get; init; }

    /// <summary>The daemon's base URL. Empty is the local daemon, forced by a lease's own <c>stateDir</c>. Null leaves the choice to the client's defaults.</summary>
    public string? DaemonBaseUrl { get; init; }

    public string? DaemonAuthToken { get; init; }

    /// <summary>The transport to the local daemon, <c>auto</c> when a lease's local daemon must not inherit a remote one.</summary>
    public string? DaemonTransport { get; init; }

    /// <summary>The lease's client configuration, as it arrived.</summary>
    public JsonObject Settings { get; init; } = [];

    /// <summary>
    /// The configuration for one session. A provider's local daemon (a lease with a <c>stateDir</c> and no daemon)
    /// must not inherit the user's remote-daemon connection, so it is pinned local.
    /// </summary>
    public static DeviceClientConfig For(string session, DeviceConnection? connection)
    {
        var local = connection?.Client?["stateDir"] is not null && connection.Daemon is null;
        return new DeviceClientConfig
        {
            Session = session,
            Settings = connection?.Client is { } client ? (JsonObject)client.DeepClone() : [],
            DaemonBaseUrl = local ? "" : connection?.Daemon?.BaseUrl,
            DaemonAuthToken = local ? "" : connection?.Daemon?.AuthToken,
            DaemonTransport = local ? "auto" : null,
        };
    }

    /// <summary>The client for this configuration: a remote daemon at its base URL, else the local daemon.</summary>
    public IDeviceClient Connect()
    {
        if (!string.IsNullOrEmpty(DaemonBaseUrl))
        {
            var remote = new DaemonEndpoint(new Uri(DaemonBaseUrl), string.IsNullOrEmpty(DaemonAuthToken) ? null : DaemonAuthToken);
            var checkedOnce = new Lazy<Task<DaemonEndpoint>>(async () =>
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                await AgentDeviceDaemon.CheckHealthAsync(remote, AgentDeviceDaemon.SharedHttp, timeout.Token).ConfigureAwait(false);
                return remote;
            });
            return new AgentDeviceRpcClient(Session, Settings, token => checkedOnce.Value.WaitAsync(token), AgentDeviceDaemon.SharedHttp);
        }

        var stateDirectory = Settings["stateDir"]?.GetValue<string>();
        return new AgentDeviceRpcClient(Session, Settings, token => AgentDeviceDaemon.LocalAsync(stateDirectory, token), AgentDeviceDaemon.SharedHttp);
    }
}
