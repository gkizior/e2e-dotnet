// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Mobile;

/// <summary>An agent-device daemon other than the local one.</summary>
/// <param name="BaseUrl">Base URL answering agent-device's <c>GET /health</c> and <c>POST /rpc</c>.</param>
/// <param name="AuthToken">Bearer token the daemon expects, when it wants one.</param>
public sealed record DeviceDaemon(string BaseUrl, string? AuthToken = null);

/// <summary>How a worker's agent-device client reaches its device: which daemon, and with what configuration.</summary>
public record DeviceConnection
{
    /// <summary>The agent-device daemon to connect to. Absent, the local one.</summary>
    public DeviceDaemon? Daemon { get; init; }

    /// <summary>
    /// Client configuration for that daemon: the lease scope a daemon resolves a hosted device by
    /// (<c>tenant</c>, <c>runId</c>, <c>leaseId</c>, <c>leaseBackend</c>, <c>leaseProvider</c>), the
    /// <c>stateDir</c> of a daemon other than the default one, provider options. JSON data only, since it travels
    /// to the worker through the environment. <c>session</c>, <c>daemonBaseUrl</c>, <c>daemonAuthToken</c>, and
    /// <c>daemonTransport</c> are not part of it.
    /// </summary>
    public JsonObject? Client { get; init; }
}

/// <summary>What one worker slot drives. An empty binding leaves every choice to the local daemon.</summary>
public sealed record SlotBinding : DeviceConnection
{
    /// <summary>Device to select by name. Absent, <see cref="DeviceId"/> selects one, or the daemon picks a booted one.</summary>
    public string? Device { get; init; }

    /// <summary>Device to select by the id agent-device lists it under: a simulator UDID or an Android serial.</summary>
    public string? DeviceId { get; init; }

    /// <summary>App a provider already installed from <c>appPath</c>, so the worker installs nothing and opens this.</summary>
    public string? InstalledApp { get; init; }

    /// <summary>The id of the provider lease the slot rides, set only for a provider's lease.</summary>
    public string? LeaseId { get; init; }

    /// <summary>
    /// App the warm-up in <c>prepare</c> opened in the slot's session, which the worker resumes: a permission
    /// command there already acts on it. Absent when nothing was opened, or the open failed.
    /// </summary>
    public string? SessionApp { get; init; }
}

/// <summary>
/// What one worker slot drives, and how it travels from <c>prepare</c> to the worker's start. Every way a pool
/// comes by devices (a configured list, the booted devices it discovered, the leases a provider granted) ends in
/// one <see cref="SlotBinding"/> per slot, read through one environment variable whatever produced it.
/// </summary>
public static class DeviceBindings
{
    /// <summary>
    /// The most the serialized bindings of one target may occupy in a worker's environment: a lease comes from
    /// third-party code, and an oversized environment fails the worker spawn with an opaque error.
    /// </summary>
    public const int MaxEnvironmentBytes = 16 * 1024;

    /// <summary>Reserved for the engine and the daemon field, where a provider's value would be silently overruled.</summary>
    private static readonly HashSet<string> ReservedClientKeys = new(["session", "daemonBaseUrl", "daemonAuthToken", "daemonTransport"], StringComparer.Ordinal);

    private static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>What a binding calls its device in progress and error lines: the name, else the id, else nothing.</summary>
    public static string? DeviceLabel(SlotBinding? binding) => binding?.Device ?? binding?.DeviceId;

    /// <summary>
    /// The app a slot's <c>app.open()</c> launches, and its warm-up opens once: the target's bundle id, else the app
    /// the build <c>appPath</c> installed once the suite's <c>device.installApp()</c> (or a device provider) has.
    /// </summary>
    public static string? PinnedApp(MobileApp app, string? installedApp) => app.BundleId ?? installedApp;

    /// <summary>
    /// The environment variable a target's bindings travel to the workers in: the name made environment-safe for
    /// reading, plus a digest of the exact name so <c>ios-a</c> and <c>ios.a</c> never share a key.
    /// </summary>
    public static string Variable(string targetName)
    {
        var readable = new string(targetName.Select(character => char.IsAsciiLetterOrDigit(character) ? char.ToUpperInvariant(character) : '_').ToArray());
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(targetName)))[..8];
        return $"E2E_AGENT_DEVICE_POOL_{readable}_{digest}";
    }

    /// <summary>Serializes the bindings for the environment: declared fields only, bounded.</summary>
    public static string Encode(IReadOnlyList<SlotBinding> bindings)
    {
        var array = new JsonArray();
        foreach (var binding in bindings)
        {
            array.Add(DeviceArgs.Of(
                ("device", Text(binding.Device)),
                ("deviceId", Text(binding.DeviceId)),
                ("daemon", binding.Daemon is null ? null : DeviceArgs.Of(("baseUrl", Text(binding.Daemon.BaseUrl)), ("authToken", Text(binding.Daemon.AuthToken)))),
                ("client", binding.Client?.DeepClone()),
                ("installedApp", Text(binding.InstalledApp)),
                ("leaseId", Text(binding.LeaseId)),
                ("sessionApp", Text(binding.SessionApp))));
        }

        var encoded = array.ToJsonString(Compact);
        var bytes = Encoding.UTF8.GetByteCount(encoded);
        if (bytes > MaxEnvironmentBytes)
        {
            throw new EngineException(
                EngineErrorCodes.EngineFailure,
                $"the device bindings take {bytes} bytes; the worker environment carries at most {MaxEnvironmentBytes}",
                retryable: false);
        }

        return encoded;
    }

    /// <summary>The bindings a <c>prepare</c> left in the environment, or null when there are none it can read.</summary>
    public static IReadOnlyList<SlotBinding>? Decode(string? raw)
    {
        if (raw is null)
        {
            return null;
        }

        try
        {
            if (JsonNode.Parse(raw) is not JsonArray array)
            {
                return null;
            }

            var bindings = new List<SlotBinding>();
            foreach (var item in array)
            {
                if (item is not JsonObject entry || !TryRead(entry, out var binding))
                {
                    return null;
                }

                bindings.Add(binding);
            }

            return bindings;
        }
        catch (JsonException)
        {
            // Not ours to read.
            return null;
        }
    }

    /// <summary>Plain JSON data with none of the reserved keys: what survives the environment and what the engine will not override.</summary>
    public static bool IsClientConfig(JsonObject? client) =>
        client is not null && client.All(pair => !ReservedClientKeys.Contains(pair.Key)) && IsJsonData(client);

    private static bool TryRead(JsonObject entry, out SlotBinding binding)
    {
        binding = new SlotBinding();
        if (!TryString(entry, "device", out var device)
            || !TryString(entry, "deviceId", out var deviceId)
            || !TryString(entry, "installedApp", out var installedApp)
            || !TryString(entry, "leaseId", out var leaseId)
            || !TryString(entry, "sessionApp", out var sessionApp))
        {
            return false;
        }

        DeviceDaemon? daemon = null;
        if (entry["daemon"] is { } daemonNode)
        {
            if (daemonNode is not JsonObject daemonObject
                || !TryString(daemonObject, "baseUrl", out var baseUrl)
                || baseUrl is null
                || !TryString(daemonObject, "authToken", out var authToken))
            {
                return false;
            }

            daemon = new DeviceDaemon(baseUrl, authToken);
        }

        JsonObject? client = null;
        if (entry["client"] is { } clientNode)
        {
            if (clientNode is not JsonObject clientObject || !IsClientConfig(clientObject))
            {
                return false;
            }

            client = (JsonObject)clientObject.DeepClone();
        }

        binding = new SlotBinding
        {
            Device = device,
            DeviceId = deviceId,
            InstalledApp = installedApp,
            LeaseId = leaseId,
            SessionApp = sessionApp,
            Daemon = daemon,
            Client = client,
        };
        return true;
    }

    private static JsonNode? Text(string? value) => value is null ? null : JsonValue.Create(value);

    /// <summary>Reads an optional string field: absent is fine, any other type is not.</summary>
    private static bool TryString(JsonObject source, string name, out string? value)
    {
        value = null;
        if (source[name] is not { } node)
        {
            return true;
        }

        if (node is JsonValue json && json.TryGetValue<string>(out var text))
        {
            value = text;
            return true;
        }

        return false;
    }

    /// <summary>What survives serialization unchanged: a non-finite number would reach the worker as null.</summary>
    private static bool IsJsonData(JsonNode? node) => node switch
    {
        null => true,
        JsonObject obj => obj.All(pair => IsJsonData(pair.Value)),
        JsonArray array => array.All(IsJsonData),
        JsonValue value when value.TryGetValue<double>(out var number) => double.IsFinite(number),
        JsonValue => true,
        _ => false,
    };
}
