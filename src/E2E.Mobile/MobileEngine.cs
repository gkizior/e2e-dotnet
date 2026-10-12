// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Mobile;

/// <summary>What <see cref="MobileEngine"/> hands the code that turns a node and an action into device commands.</summary>
/// <remarks>
/// Upstream's <c>AgentDeviceSurface</c> owns these members next to observe and the session hooks. <see cref="DeviceActions"/> is
/// the implementation the engine uses unless a caller sets another through <see cref="MobileEngine.Actions"/>.
/// </remarks>
public interface IDeviceActions
{
    Task PerformAsync(DeviceSession session, SemanticNode node, LocatorAction action, CancellationToken cancellationToken);

    Task PressAsync(DeviceSession session, string key, CancellationToken cancellationToken);

    Task SwipeAsync(DeviceSession session, ScrollDirection direction, CancellationToken cancellationToken);

    Task<EngineScreenshot> ScreenshotAsync(DeviceSession session, CancellationToken cancellationToken);
}

/// <summary>What a worker's start is given: which target and worker slot it serves, and the environment <c>prepare</c> left.</summary>
public sealed record MobileStartInfo
{
    public string RunId { get; init; } = "";

    public required string TargetName { get; init; }

    public int WorkerSlot { get; init; }

    public string ProjectRoot { get; init; } = Directory.GetCurrentDirectory();

    /// <summary>The environment the worker reads its binding from, never the process environment.</summary>
    public IReadOnlyDictionary<string, string?> Env { get; init; } = new Dictionary<string, string?>();

    public Action<string> Log { get; init; } = static _ => { };
}

/// <summary>
/// The agent-device engine for e2e: a mobile body that implements the core engine contract, one device session per
/// worker, over an <see cref="IDeviceClient"/>. A test launches the pinned app with <c>app.open()</c>, which is the
/// fresh launch of the pinned app. The device is a simulator, an emulator, or a connected phone, named by the options,
/// discovered, or leased from an <see cref="IDeviceProvider"/>.
/// </summary>
public sealed class MobileEngine : IEngine
{
    /// <summary>The version of the engine, as upstream's <c>@e2e-dev/mobile</c> numbers the surface this one follows.</summary>
    public const string EngineVersion = "0.11.0";

    private static readonly Dictionary<MobilePlatform, string> LoopbackNotes = new()
    {
        [MobilePlatform.Ios] = "a simulator shares this machine's loopback, and a hosted device cannot reach it at all",
        [MobilePlatform.Android] = "127.0.0.1 on an Android emulator is the emulator itself (this machine is 10.0.2.2), and a hosted device cannot reach this machine's loopback at all",
    };

    private readonly DeviceClientFactory _createClient;

    /// <summary>Creates the engine over the local agent-device daemon, or the daemon a lease names. Needs Node 22.12 or newer at run time.</summary>
    public MobileEngine(MobileOptions options)
        : this(options, static (session, connection) => DeviceClientConfig.For(session, connection).Connect())
    {
    }

    /// <summary>Creates the engine over a client of the caller's making: the seam unit tests script.</summary>
    internal MobileEngine(MobileOptions options, DeviceClientFactory createClient)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(createClient);
        Validate(options);
        Options = options;
        _createClient = createClient;
        Pool = new DevicePool(options, createClient);
    }

    public MobileOptions Options { get; }

    /// <summary>The target's devices, one per worker slot.</summary>
    public DevicePool Pool { get; }

    /// <summary>The code that performs node actions, keys, scrolls, and screenshots.</summary>
    public IDeviceActions Actions { get; init; } = new DeviceActions();

    /// <summary>The clock the transition budget counts on. Tests replace it with one the delays advance.</summary>
    internal TimeProvider Clock { get; init; } = TimeProvider.System;

    private readonly List<DeviceSession> _sessions = [];

    /// <summary>The sessions this engine started that have not closed: the attempts running on it right now.</summary>
    public IReadOnlyList<DeviceSession> ActiveSessions
    {
        get
        {
            lock (_sessions)
            {
                return [.. _sessions];
            }
        }
    }

    internal void Register(DeviceSession session)
    {
        lock (_sessions)
        {
            _sessions.Add(session);
        }
    }

    internal void Unregister(DeviceSession session)
    {
        lock (_sessions)
        {
            _sessions.Remove(session);
        }
    }

    /// <summary>The wait between two attempts at a sparse snapshot. Tests replace it to spend no time.</summary>
    internal Func<TimeSpan, CancellationToken, Task> Delay { get; init; } = static (span, token) => Task.Delay(span, token);

    public string Platform => Options.Platform.WireName();

    public string Version => EngineVersion;

    public EngineCapabilities Capabilities =>
        EngineCapabilities.Observation
        | EngineCapabilities.Actions
        | EngineCapabilities.Location
        | EngineCapabilities.Keyboard
        | EngineCapabilities.Scroll
        | EngineCapabilities.History
        | EngineCapabilities.Screenshot;

    /// <summary>
    /// A device target launches an installed app or a build: one of the bundle id and the build path is required. A URL
    /// (a website in the device's browser) is not supported yet, and a link as the bundle id is a mistake: a test opens
    /// one with <c>device.openLink</c>.
    /// </summary>
    public static void ValidateApp(MobileApp app, string? url, string targetName, MobilePlatform platform)
    {
        ArgumentNullException.ThrowIfNull(app);
        var where = $"target \"{targetName}\"";
        if (url is not null)
        {
            throw new ConfigurationException(
                "INVALID_CONFIG",
                $"{where} declares app.url, but a mobile() target cannot open a website in the device's browser yet; test the installed app with app.bundleId or app.appPath. When it lands, note that {LoopbackNotes[platform]}");
        }

        if (app.BundleId is null && app.AppPath is null)
        {
            throw new ConfigurationException(
                "INVALID_CONFIG",
                $"{where} needs app.bundleId or app.appPath: a mobile() target launches an installed app, targets: [{{ engine: mobile({{ platform }}), app: {{ bundleId: 'com.example.app' }} }}]");
        }

        if (app.BundleId is not null && DeviceLinks.IsLink(app.BundleId))
        {
            throw new ConfigurationException(
                "INVALID_CONFIG",
                $"{where} app.bundleId names an app by bundle id, package, or display name, not a link; a test opens a deep link or web link with device.openLink");
        }
    }

    /// <summary>
    /// Binds a device to every worker slot the run will use and warms each, once per run and target, before any worker
    /// starts. See <see cref="DevicePool.PrepareAsync"/>. The result's environment is what the workers read.
    /// </summary>
    public Task<MobilePrepareResult> PrepareAsync(MobilePrepareInfo info, CancellationToken cancellationToken) =>
        Pool.PrepareAsync(info, cancellationToken);

    /// <summary>Closes the sessions prepare opened and releases the leases, once per run and target.</summary>
    public Task FinishAsync(MobileFinishInfo info, CancellationToken cancellationToken) =>
        Pool.FinishAsync(info, cancellationToken);

    public async Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ValidateApp(Options.App, options.BaseUrl, Options.TargetName, Options.Platform);
        return await StartAsync(new MobileStartInfo { TargetName = Options.TargetName, ProjectRoot = options.ProjectRoot ?? Directory.GetCurrentDirectory() }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Starts the worker of one slot: boots its device under its session and resumes the app prepare opened there.</summary>
    public async Task<DeviceSession> StartAsync(MobileStartInfo info, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(info);
        return await DeviceSession.StartAsync(this, info, _createClient, cancellationToken).ConfigureAwait(false);
    }

    private static void Validate(MobileOptions options)
    {
        if (new object?[] { options.Device, options.Devices, options.Provider }.Count(choice => choice is not null) > 1)
        {
            throw new ConfigurationException("INVALID_CONFIG", "mobile: set at most one of `Device`, `Devices`, and `Provider`");
        }

        if (options.Provider is { } provider && string.IsNullOrWhiteSpace(provider.Name))
        {
            throw new ConfigurationException("INVALID_CONFIG", "mobile: a `device` provider needs a non-empty `name`");
        }

        if (options.SettleMilliseconds < 0)
        {
            throw new ConfigurationException("INVALID_CONFIG", "mobile: `settle` must be a non-negative integer of milliseconds, or false");
        }

        if (options.TransitionMilliseconds < 0)
        {
            throw new ConfigurationException("INVALID_CONFIG", "mobile: `transition` must be a non-negative integer of milliseconds");
        }

        if (options.Device is not null && options.Device.Length == 0)
        {
            throw new ConfigurationException("INVALID_CONFIG", "mobile: `device` is an empty name; name a simulator or emulator, or omit it to use every booted one");
        }
    }
}
