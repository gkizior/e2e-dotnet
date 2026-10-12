// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Mobile;

/// <summary>What <see cref="MobileEngine.PrepareAsync"/> is given, once per run and target, before any worker exists.</summary>
public sealed record MobilePrepareInfo
{
    public required string RunId { get; init; }

    public required string TargetName { get; init; }

    public required string ProjectRoot { get; init; }

    /// <summary>Worker slots the run will use for this target.</summary>
    public required int Slots { get; init; }

    public required IReadOnlyDictionary<string, string?> Env { get; init; }

    public required MobileApp App { get; init; }

    public required Action<string> Log { get; init; }
}

/// <summary>What prepare reports back: the workers the target can use, and the environment its workers read.</summary>
public sealed record MobilePrepareResult
{
    /// <summary>The worker cap of the target, one per bound device. Null when prepare bound nothing.</summary>
    public int? Workers { get; init; }

    public IReadOnlyDictionary<string, string> Env { get; init; } = new Dictionary<string, string>();
}

/// <summary>What <see cref="MobileEngine.FinishAsync"/> is given, once per run and target, after the last worker ended.</summary>
public sealed record MobileFinishInfo
{
    public required string RunId { get; init; }

    public required string TargetName { get; init; }

    public required IReadOnlyDictionary<string, string?> Env { get; init; }

    public required Action<string> Log { get; init; }

    /// <summary>How long finish may take before it gives up on a close, which it does without failing.</summary>
    public TimeSpan CleanupTimeout { get; init; } = E2EDefaults.CleanupTimeout;
}

/// <summary>
/// Where a pool's devices come from. <see cref="BindAsync"/> runs once per run and target in prepare;
/// <see cref="FinishAsync"/> releases what bind acquired, when anything was.
/// </summary>
internal interface IDeviceSource
{
    /// <summary>Workers served per target, one per device, when known before prepare.</summary>
    int? Size { get; }

    /// <summary>What a worker drives when its run had no prepare. Null leaves the choice to the local daemon.</summary>
    IReadOnlyList<SlotBinding>? Fallback { get; }

    /// <summary>One binding per worker slot the run will use, at most <see cref="MobilePrepareInfo.Slots"/> and never none.</summary>
    Task<IReadOnlyList<SlotBinding>> BindAsync(MobilePrepareInfo info, CancellationToken cancellationToken);

    Task FinishAsync(MobileFinishInfo info, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>The fields agent-device commands select a device by: the platform, then one of a name, a simulator UDID, or an Android serial.</summary>
public sealed record DeviceSelection(MobilePlatform Platform, string? Device = null, string? Udid = null, string? Serial = null)
{
    /// <summary>The selection as command options, with any further options beside it.</summary>
    public JsonObject ToArgs(params (string Key, JsonNode? Value)[] more) =>
        DeviceArgs.Of(
            [
                ("platform", JsonValue.Create(Platform.WireName())),
                ("device", Device is null ? null : JsonValue.Create(Device)),
                ("udid", Udid is null ? null : JsonValue.Create(Udid)),
                ("serial", Serial is null ? null : JsonValue.Create(Serial)),
                .. more,
            ]);
}

/// <summary>
/// The devices one target drives: one simulator or emulator per worker slot, each under its own agent-device
/// session. A named device is a pool of one and a list a pool of many. With no device the pool is every booted
/// device of the platform, discovered in prepare. A <see cref="IDeviceProvider"/> leases one hosted device per slot
/// instead. Prepare binds one <see cref="SlotBinding"/> per slot, warms each once per run before any worker exists,
/// and hands the bindings to the workers through the run's environment.
/// </summary>
public sealed partial class DevicePool
{
    private readonly MobileOptions _options;
    private readonly IDeviceSource _source;

    /// <summary>What this process's prepare bound, per target: a pool may serve several targets and runs.</summary>
    private readonly Dictionary<string, IReadOnlyList<SlotBinding>> _bound = [];

    /// <summary>The clients warm opened sessions with, per target, held for finish to close whatever warm got to.</summary>
    private readonly Dictionary<string, List<IDeviceClient>> _warmed = [];

    private readonly DeviceClientFactory _createClient;

    internal DevicePool(MobileOptions options, DeviceClientFactory createClient)
    {
        _options = options;
        _createClient = createClient;
        _source = SourceFor(options, createClient);
    }

    /// <summary>
    /// Workers the engine serves per target, one per device: known up front for a configured pool, and null for a
    /// discovered or leased one until prepare reports it.
    /// </summary>
    public int? Size => _source.Size;

    /// <summary>The agent-device session a worker slot drives its device under: the session option or <c>e2e-&lt;target&gt;</c>, then <c>-&lt;slot&gt;</c>.</summary>
    public string Session(string targetName, int slot) => $"{_options.Session ?? $"e2e-{targetName}"}-{slot}";

    /// <summary>
    /// What a worker slot drives, for a worker's start: what this process's prepare bound for the target, else what
    /// prepare left in the environment, else the source's fallback for a run without prepare. With none, the choice
    /// stays with the local daemon. A slot beyond the pool is a broken invariant.
    /// </summary>
    public SlotBinding? Binding(string targetName, int slot, IReadOnlyDictionary<string, string?> env)
    {
        IReadOnlyList<SlotBinding>? bindings;
        lock (_bound)
        {
            bindings = _bound.GetValueOrDefault(targetName);
        }

        bindings ??= DeviceBindings.Decode(env.GetValueOrDefault(DeviceBindings.Variable(targetName))) ?? _source.Fallback;
        if (bindings is null)
        {
            return null;
        }

        if (slot >= bindings.Count)
        {
            throw new EngineException(
                EngineErrorCodes.EngineFailure,
                $"worker slot {slot} is outside a device pool of {bindings.Count}; the runner must cap the target at the engine's declared workers",
                retryable: false);
        }

        return bindings[slot];
    }

    /// <summary>
    /// Binds a device to every worker slot the run will use and warms each, once per run and outside every launch
    /// budget, so a worker's start finds a booted device. The bindings are reported back as the target's worker cap
    /// and left in the environment for the workers. A target nothing runs on binds nothing: a hosted session is
    /// billed from the moment it starts.
    /// </summary>
    public async Task<MobilePrepareResult> PrepareAsync(MobilePrepareInfo info, CancellationToken cancellationToken)
    {
        if (info.Slots == 0)
        {
            return new MobilePrepareResult();
        }

        var bindings = await _source.BindAsync(info, cancellationToken).ConfigureAwait(false);
        await WarmAsync(bindings, info, cancellationToken).ConfigureAwait(false);
        lock (_bound)
        {
            _bound[info.TargetName] = bindings;
        }

        return new MobilePrepareResult
        {
            Workers = bindings.Count,
            Env = new Dictionary<string, string> { [DeviceBindings.Variable(info.TargetName)] = DeviceBindings.Encode(bindings) },
        };
    }

    /// <summary>
    /// Closes every session warm opened for the target and releases what the source acquired. A worker closes its
    /// own session when it ends, but a run that failed in prepare never had one, and an agent-device session outlives
    /// the process: left open, the next run resumes it by name with the runner state that failed this one. The closes
    /// are best-effort under the hook's budget and run beside the release, so a close that hangs or fails never keeps
    /// a leased device billed.
    /// </summary>
    public async Task FinishAsync(MobileFinishInfo info, CancellationToken cancellationToken)
    {
        lock (_bound)
        {
            _bound.Remove(info.TargetName);
        }

        List<IDeviceClient> clients;
        lock (_warmed)
        {
            _warmed.Remove(info.TargetName, out var held);
            clients = held ?? [];
        }

        var closing = CleanupBudget.WithinAsync(
            token => Task.WhenAll(clients.Select(client => CloseQuietlyAsync(client, token))),
            info.CleanupTimeout,
            cancellationToken);
        await Task.WhenAll(closing, _source.FinishAsync(info, cancellationToken)).ConfigureAwait(false);
    }

    private static async Task CloseQuietlyAsync(IDeviceClient client, CancellationToken cancellationToken)
    {
        try
        {
            await client.CallAsync(DeviceMethods.SessionsClose, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best effort: a close that fails leaves nothing more to do.
        }
    }

    /// <summary>The source the device option names: a configured pool, the booted devices, or a provider's leases.</summary>
    private IDeviceSource SourceFor(MobileOptions options, DeviceClientFactory createClient)
    {
        if (options.Provider is { } provider)
        {
            return new LeasedDevices(provider, options.Platform);
        }

        if (options.Devices is { } devices)
        {
            return devices.Count == 0
                ? throw new ConfigurationException(
                    "INVALID_CONFIG",
                    "mobile: `device` is an empty pool; name at least one simulator or emulator, or omit it to use every booted one")
                : new ConfiguredDevices(devices);
        }

        return options.Device is { } device
            ? new ConfiguredDevices([device])
            : new BootedDevices(options.Platform, createClient, (targetName, slot) => Session(targetName, slot));
    }

    /// <summary>
    /// Boots every bound device, starts the iOS automation runner on it, and opens the pinned app on it once, so a
    /// test that starts without <c>app.open()</c> finds it on screen. A plain foreground open, with none of the
    /// engine's launch options. One slot after another, on purpose: workers booting at once contend for the host and
    /// the daemon, and one cold boot pushes the others
    /// past the launch timeout. A device that cannot boot ends the run here. An app that does not open is
    /// logged and left to the first attempt. An automation runner that is busy or wedged from an earlier run ends the
    /// run here too: the first attempt could only meet the same runner and fail its first observation with the app
    /// blamed. A runner that did not start leaves the slot unopened: the open would start it again under a shorter
    /// budget. A build <c>appPath</c> is not on the device yet, so that slot boots and starts the runner only, unless a
    /// lease says the build is already there.
    /// </summary>
    private async Task WarmAsync(IReadOnlyList<SlotBinding> bindings, MobilePrepareInfo info, CancellationToken cancellationToken)
    {
        for (var slot = 0; slot < bindings.Count; slot++)
        {
            var binding = bindings[slot];
            var label = DeviceBindings.DeviceLabel(binding) ?? $"a booted {_options.Platform.WireName()} device";
            var where = DeviceSelectionFor(_options.Platform, binding);
            var session = Session(info.TargetName, slot);
            var at = $"session {session} on {label}";
            var client = _createClient(session, binding);
            Retain(info.TargetName, client);
            info.Log($"booting {label} ({slot + 1} of {bindings.Count})");
            await DeviceErrors.RunCommandAsync(
                "boot",
                token => client.CallAsync(DeviceMethods.DevicesBoot, where.ToArgs(), token),
                cancellationToken,
                at).ConfigureAwait(false);
            var runnerUp = _options.Platform != MobilePlatform.Ios || await PrepareRunnerAsync(client, where, info, at, cancellationToken).ConfigureAwait(false);
            var app = DeviceBindings.PinnedApp(info.App, binding.InstalledApp);
            if (app is null || !runnerUp)
            {
                continue;
            }

            // A build the suite installs itself is not on the device yet, so there is nothing to open: the first attempt installs it.
            if (info.App.AppPath is not null && binding.InstalledApp is null)
            {
                info.Log($"{label}: {app} awaits the suite's device.installApp()");
                continue;
            }

            try
            {
                await DeviceErrors.RunCommandAsync(
                    $"open {app}",
                    token => client.CallAsync(DeviceMethods.AppsOpen, where.ToArgs(("app", JsonValue.Create(app))), token),
                    cancellationToken,
                    at).ConfigureAwait(false);
            }
            catch (Exception cause) when (!cancellationToken.IsCancellationRequested && !DeviceErrors.IsRunnerFailure(cause))
            {
                info.Log($"{label}: {app} did not open ({DeviceErrors.Message(cause)}); the first attempt opens it");
            }
        }
    }

    /// <summary>
    /// Starts the slot's iOS automation runner under <c>prepare ios-runner</c>, whose startup budget covers a cold
    /// simulator. Left to the first open, the start runs inside that request's 90 s envelope, which a cold runner on a
    /// loaded CI Mac outlasts. A runner that is busy or wedged ends the run
    /// here. Any other failure is logged and left to the first attempt. True when the runner is up.
    /// </summary>
    private static async Task<bool> PrepareRunnerAsync(IDeviceClient client, DeviceSelection where, MobilePrepareInfo info, string at, CancellationToken cancellationToken)
    {
        try
        {
            await DeviceErrors.RunCommandAsync(
                "prepare ios-runner",
                token => client.CallAsync(DeviceMethods.CommandPrepare, where.ToArgs(("action", JsonValue.Create("ios-runner"))), token),
                cancellationToken,
                at).ConfigureAwait(false);
            return true;
        }
        catch (Exception cause) when (!cancellationToken.IsCancellationRequested && !DeviceErrors.IsRunnerFailure(cause))
        {
            info.Log($"{at}: automation runner not prepared ({DeviceErrors.Message(cause)}); the first attempt starts it");
            return false;
        }
    }

    /// <summary>Holds a warm-up client for finish, before its first command: a boot that fails still opened the session.</summary>
    private void Retain(string targetName, IDeviceClient client)
    {
        lock (_warmed)
        {
            if (!_warmed.TryGetValue(targetName, out var clients))
            {
                _warmed[targetName] = clients = [];
            }

            clients.Add(client);
        }
    }

    /// <summary>
    /// The selection agent-device commands take for a binding: an id the pool discovered goes under the platform's
    /// id field (<c>udid</c> for a simulator, <c>serial</c> for an Android device), a configured string too when it
    /// reads as a UDID or an emulator serial, and any other string is a device name.
    /// </summary>
    public static DeviceSelection DeviceSelectionFor(MobilePlatform platform, SlotBinding? binding)
    {
        var device = binding?.Device;
        var id = binding?.DeviceId ?? (device is not null && (Udid().IsMatch(device) || EmulatorSerial().IsMatch(device)) ? device : null);
        if (id is null)
        {
            return new DeviceSelection(platform, device);
        }

        return platform == MobilePlatform.Android ? new DeviceSelection(platform, Serial: id) : new DeviceSelection(platform, Udid: id);
    }

    /// <summary>An Apple simulator UDID.</summary>
    [GeneratedRegex(@"^[0-9A-F]{8}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{4}-[0-9A-F]{12}\z", RegexOptions.IgnoreCase)]
    private static partial Regex Udid();

    /// <summary>An Android emulator's adb serial.</summary>
    [GeneratedRegex(@"^emulator-\d+\z")]
    private static partial Regex EmulatorSerial();
}

/// <summary>The devices named in the config, bound in order, as many as the run has slots.</summary>
internal sealed class ConfiguredDevices : IDeviceSource
{
    public ConfiguredDevices(IReadOnlyList<string> devices)
    {
        Size = devices.Count;
        Fallback = [.. devices.Select(device => new SlotBinding { Device = device })];
    }

    public int? Size { get; }

    public IReadOnlyList<SlotBinding>? Fallback { get; }

    public Task<IReadOnlyList<SlotBinding>> BindAsync(MobilePrepareInfo info, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SlotBinding>>([.. Fallback!.Take(Math.Min(info.Slots, Fallback!.Count))]);
}

/// <summary>Every booted device of the platform, as many as the run has slots. With none booted, one slot the daemon fills.</summary>
internal sealed class BootedDevices(MobilePlatform platform, DeviceClientFactory createClient, Func<string, int, string> session) : IDeviceSource
{
    public int? Size => null;

    public IReadOnlyList<SlotBinding>? Fallback => null;

    public async Task<IReadOnlyList<SlotBinding>> BindAsync(MobilePrepareInfo info, CancellationToken cancellationToken)
    {
        var booted = await InventoryAsync(info.TargetName, cancellationToken).ConfigureAwait(false);
        var chosen = booted.Take(info.Slots).ToList();
        if (chosen.Count == 0)
        {
            info.Log($"no booted {platform.WireName()} device; agent-device boots one");
            return [new SlotBinding()];
        }

        info.Log($"{booted.Count} booted {platform.WireName()} device(s); driving {chosen.Count}: {string.Join(", ", chosen.Select(device => device.Name))}");
        return [.. chosen.Select(device => new SlotBinding { DeviceId = device.Id })];
    }

    /// <summary>Every booted device of the platform, by stable id and name, in agent-device's inventory order.</summary>
    private async Task<List<(string Id, string Name)>> InventoryAsync(string targetName, CancellationToken cancellationToken)
    {
        var client = createClient(session(targetName, 0), null);
        var inventory = await DeviceErrors.RunCommandAsync(
            "devices",
            token => client.CallAsync(DeviceMethods.DevicesList, DeviceArgs.Of(("platform", JsonValue.Create(platform.WireName()))), token),
            cancellationToken).ConfigureAwait(false);
        if (inventory is not JsonArray devices)
        {
            return [];
        }

        var result = new List<(string, string)>();
        foreach (var node in devices)
        {
            if (node is JsonObject device
                && Text(device["id"]) is { } id
                && device["booted"] is JsonValue { } booted && booted.TryGetValue<bool>(out var isBooted) && isBooted
                && Text(device["platform"]) == platform.WireName())
            {
                result.Add((id, Text(device["name"]) ?? id));
            }
        }

        return result;
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
}
