// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;

namespace E2E.Mobile.Limrun;

/// <summary>
/// Limrun for <see cref="MobileOptions.Provider"/>: one hosted iOS simulator or Android emulator per worker slot, each
/// driven through an agent-device daemon of its own that the provider starts and stops. It authenticates with
/// <c>LIMRUN_API_KEY</c> from the run's environment (and <c>LIMRUN_BASE_URL</c> to reach another API), installs the
/// target's build when it names one, keeps the device's lease alive while other targets run, and deletes the device
/// on release. An Android device needs the Android SDK Platform-Tools <c>adb</c> on the path, for the daemon's tunnel.
/// </summary>
public sealed class LimrunSimulators : IDeviceProvider
{
    private const string DefaultBaseUrl = "https://api.limrun.com";

    /// <summary>How long a device may take to become ready.</summary>
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan ReadyPoll = TimeSpan.FromMilliseconds(500);

    private readonly LimrunSimulatorsOptions _options;
    private readonly ILimrunHost _host;
    private readonly HttpClient _http;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly ConcurrentDictionary<string, Held> _held = new(StringComparer.Ordinal);

    public LimrunSimulators(LimrunSimulatorsOptions? options = null)
        : this(options, LimrunHost.Instance, AgentDeviceDaemon.SharedHttp, static (delay, token) => Task.Delay(delay, token))
    {
    }

    internal LimrunSimulators(LimrunSimulatorsOptions? options, ILimrunHost host, HttpClient http, Func<TimeSpan, CancellationToken, Task> delay)
    {
        _options = options ?? new LimrunSimulatorsOptions();
        _host = host;
        _http = http;
        _delay = delay;
    }

    public string Name => "limrun";

    /// <summary>What a device holds until release: its daemon's directory and environment, the API it was created with, and its heartbeat.</summary>
    private sealed class Held(string stateDirectory, IReadOnlyDictionary<string, string?> environment, LimrunApi api, MobilePlatform platform)
    {
        public string StateDirectory { get; } = stateDirectory;

        public IReadOnlyDictionary<string, string?> Environment { get; set; } = environment;

        public LimrunApi Api { get; } = api;

        public MobilePlatform Platform { get; } = platform;

        public Func<Task>? StopHeartbeat { get; set; }

        /// <summary>Whether the daemon start was attempted, so there may be a daemon to stop.</summary>
        public bool DriverStarted { get; set; }
    }

    public async Task<DeviceLease> AcquireAsync(DeviceRequest request, CancellationToken cancellationToken)
    {
        // The daemon the provider starts is the pinned release, and a client that speaks another would read it wrongly.
        if (request.AgentDeviceVersion != AgentDeviceVersion.Pinned)
        {
            throw new InvalidOperationException($"LimrunSimulators starts agent-device {AgentDeviceVersion.Pinned}, but the engine asked for {request.AgentDeviceVersion}.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        var api = ApiFor(request.Env);
        if (request.Platform == MobilePlatform.Android)
        {
            await _host.AssertAndroidToolsAsync(request.Env, cancellationToken).ConfigureAwait(false);
        }

        var created = await api.CreateAsync(request.Platform, CreateBody(request), CancellationToken.None).ConfigureAwait(false);
        // The id is known even when the run is interrupted during the create, so cleanup can delete it.
        var id = created.Id;
        Held? resource = null;
        try
        {
            var stateDirectory = Directory.CreateTempSubdirectory("e2e-limrun-").FullName;
            resource = new Held(stateDirectory, request.Env, api, request.Platform);
            _held[id] = resource;
            request.Log($"created {id}");
            using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            budget.CancelAfter(ReadyTimeout);
            var instance = await ReadyAsync(api, request.Platform, created, budget.Token).ConfigureAwait(false);
            resource.DriverStarted = true;
            resource.Environment = DriverEnvironment(request.Env, instance, request.Platform);
            var binding = await _host.StartDriverAsync(
                new LimrunDriverRequest(stateDirectory, request.RunId, request.Slot, request.Platform, request.App, request.AppPath),
                resource.Environment,
                cancellationToken).ConfigureAwait(false);
            resource.StopHeartbeat = await _host.StartHeartbeatAsync(binding, request.Log, cancellationToken).ConfigureAwait(false);
            return new DeviceLease { Id = id, Client = binding.Client, DeviceId = binding.DeviceId, InstalledApp = binding.InstalledApp };
        }
        catch (Exception cause)
        {
            // Not the request's token: cleanup is owed even after the run was interrupted.
            using var cleanup = new CancellationTokenSource(CleanupTimeout);
            try
            {
                if (resource is not null)
                {
                    await ReleaseAsync(id, resource, cleanup.Token).ConfigureAwait(false);
                }
                else
                {
                    await DeleteAsync(api, request.Platform, id, cleanup.Token).ConfigureAwait(false);
                }
            }
            catch (Exception cleanupCause)
            {
                throw new AggregateException($"Limrun acquisition failed; cleanup also failed for {id}", cause, cleanupCause);
            }

            throw;
        }
    }

    public async Task ReleaseAsync(DeviceLease lease, DeviceReleaseContext context, CancellationToken cancellationToken)
    {
        if (_held.TryGetValue(lease.Id, out var resource))
        {
            await ReleaseAsync(lease.Id, resource, cancellationToken).ConfigureAwait(false);
        }
    }

    private LimrunApi ApiFor(IReadOnlyDictionary<string, string?> environment)
    {
        var apiKey = Variable(environment, "LIMRUN_API_KEY");
        if (apiKey is null)
        {
            throw new ConfigurationException("INVALID_CONFIG", "LimrunSimulators requires LIMRUN_API_KEY in the run environment");
        }

        return new LimrunApi(_http, apiKey, new Uri(Variable(environment, "LIMRUN_BASE_URL") ?? DefaultBaseUrl));
    }

    private JsonObject CreateBody(DeviceRequest request)
    {
        var spec = new JsonObject { ["hardTimeout"] = _options.HardTimeout ?? "1h" };
        if (_options.Region is not null)
        {
            spec["region"] = _options.Region;
        }

        if (request.Platform == MobilePlatform.Ios)
        {
            if (_options.Ios?.Model is { } model)
            {
                spec["model"] = model == LimrunIosModel.Ipad ? "ipad" : "iphone";
            }
        }
        else
        {
            if (_options.Android?.Model is { } model)
            {
                spec["model"] = model == LimrunAndroidModel.Tablet ? "tablet" : "phone";
            }

            spec["clues"] = new JsonArray(new JsonObject { ["kind"] = "OSVersion", ["osVersion"] = _options.Android?.OsVersion ?? "15" });
        }

        return new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["displayName"] = $"e2e {request.TargetName} worker {request.Slot + 1}",
                ["labels"] = new JsonObject { ["source"] = "e2e", ["run"] = request.RunId, ["target"] = request.TargetName, ["slot"] = request.Slot.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            },
            ["spec"] = spec,
        };
    }

    private async Task<LimrunInstance> ReadyAsync(LimrunApi api, MobilePlatform platform, LimrunInstance instance, CancellationToken cancellationToken)
    {
        var current = instance;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (current.State == "ready")
            {
                return current;
            }

            if (current.State == "terminated")
            {
                throw new InvalidOperationException($"Limrun instance {instance.Id} terminated before readiness: {current.ErrorMessage ?? current.TerminationReason ?? "unknown"}");
            }

            await _delay(ReadyPoll, cancellationToken).ConfigureAwait(false);
            try
            {
                current = await api.GetAsync(platform, instance.Id, cancellationToken).ConfigureAwait(false);
            }
            catch (LimrunApiException cause) when (cause.Status == HttpStatusCode.NotFound)
            {
                // The regional create can precede the backend read model. The ready budget bounds the wait.
            }
        }
    }

    private static async Task DeleteAsync(LimrunApi api, MobilePlatform platform, string id, CancellationToken cancellationToken)
    {
        try
        {
            await api.DeleteAsync(platform, id, cancellationToken).ConfigureAwait(false);
        }
        catch (LimrunApiException cause) when (cause.Status == HttpStatusCode.NotFound)
        {
            // An expired or already deleted instance needs no further cleanup.
        }
    }

    /// <summary>Stops the heartbeat, then the daemon and the device together, so one failing leaves the other cleaned. The device is held until both are.</summary>
    private async Task ReleaseAsync(string id, Held resource, CancellationToken cancellationToken)
    {
        if (resource.StopHeartbeat is { } stopHeartbeat)
        {
            await stopHeartbeat().ConfigureAwait(false);
        }

        var results = await Task.WhenAll(
            Settle(async () =>
            {
                // A device that never became ready has no daemon to stop.
                if (resource.DriverStarted)
                {
                    await _host.StopDriverAsync(resource.StateDirectory, resource.Environment, cancellationToken).ConfigureAwait(false);
                }


                // `daemon stop --clean` may have removed it already.
                if (Directory.Exists(resource.StateDirectory))
                {
                    Directory.Delete(resource.StateDirectory, recursive: true);
                }
            }),
            Settle(() => DeleteAsync(resource.Api, resource.Platform, id, cancellationToken))).ConfigureAwait(false);
        var errors = results.OfType<Exception>().ToArray();
        if (errors.Length > 0)
        {
            throw new AggregateException($"Could not fully release Limrun instance {id}: {string.Join("; ", errors.Select(error => error.Message))}", errors);
        }

        _held.TryRemove(id, out _);
    }

    private static async Task<Exception?> Settle(Func<Task> work)
    {
        try
        {
            await work().ConfigureAwait(false);
            return null;
        }
        catch (Exception cause)
        {
            return cause;
        }
    }

    /// <summary>
    /// The environment the device's daemon starts with: the run's, minus any instance address or daemon setting of the
    /// user's that would select another device, plus this instance's address and token. The user's daemon policy stays,
    /// as the policy applies to the daemon and its clients alike.
    /// </summary>
    private static Dictionary<string, string?> DriverEnvironment(IReadOnlyDictionary<string, string?> environment, LimrunInstance instance, MobilePlatform platform)
    {
        var result = new Dictionary<string, string?>(environment, StringComparer.Ordinal);
        foreach (var key in result.Keys.ToArray())
        {
            if (key == "AGENT_DEVICE_DAEMON_POLICY")
            {
                continue;
            }

            if (key.StartsWith("LIM_IOS_INSTANCE_", StringComparison.Ordinal) || key.StartsWith("LIM_ANDROID_INSTANCE_", StringComparison.Ordinal) || key.StartsWith("AGENT_DEVICE_", StringComparison.Ordinal))
            {
                result.Remove(key);
            }
        }

        if (instance.ApiUrl is not { Length: > 0 } apiUrl || instance.Token is not { Length: > 0 } token)
        {
            throw new InvalidOperationException($"Limrun instance {instance.Id} has no control endpoint or token. Use an API key with full control.");
        }

        var prefix = platform == MobilePlatform.Ios ? "LIM_IOS_INSTANCE" : "LIM_ANDROID_INSTANCE";
        result[$"{prefix}_URL"] = apiUrl;
        result[$"{prefix}_TOKEN"] = token;
        if (platform == MobilePlatform.Android)
        {
            result["LIM_ANDROID_INSTANCE_ADB_URL"] = instance.AdbWebSocketUrl is { Length: > 0 } adbUrl
                ? adbUrl
                : throw new InvalidOperationException($"Limrun instance {instance.Id} has no ADB endpoint");
        }

        result["LIMRUN_KEEP_ALIVE"] = "true";
        return result;
    }

    private static string? Variable(IReadOnlyDictionary<string, string?> environment, string name) =>
        environment.TryGetValue(name, out var value) && value?.Trim() is { Length: > 0 } trimmed ? trimmed : null;
}
