// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Mobile;

/// <summary>
/// One simulator or emulator session, driven through an <see cref="IDeviceClient"/>, exposed to the runner as the
/// contract's session. Its device and session come from the target's <see cref="DevicePool"/>, by worker slot. Every
/// observation projects the device's accessibility tree under one root of a stable id
/// (<see cref="SnapshotProjection.RootId"/>), and its node refs are valid for that observation only. It owns the id
/// space (one fresh generation per observation), the transition state, and every translation between the contract's
/// vocabulary and agent-device's commands. The actions themselves are <see cref="DeviceActions"/>.
/// </summary>
public sealed partial class DeviceSession : IEngineSession
{
    /// <summary>
    /// The Apple runner defers a full snapshot after slow accessibility work and returns a sparse one-node tree.
    /// Retrying device-side costs under a second. Returning the sparse tree costs the model a confused turn.
    /// </summary>
    private static readonly int[] SparseRetryBackoffMilliseconds = [0, 1_200, 3_000];

    private static readonly JsonSerializerOptions NodeJson = new() { PropertyNameCaseInsensitive = true };

    private readonly MobileEngine _engine;
    private readonly string _name;
    private readonly string _where;
    private readonly SlotBinding? _binding;
    private readonly MobileApp _app;
    private readonly string _projectRoot;
    private IDeviceClient? _client;
    private int _idCounter;
    private string? _appIdentity;
    private ViewportSize? _knownViewport;

    private DeviceSession(MobileEngine engine, MobileStartInfo info, SlotBinding? binding, string name, string where, IDeviceClient client)
    {
        _engine = engine;
        _name = name;
        _app = engine.Options.App;
        _projectRoot = info.ProjectRoot;
        _binding = binding;
        _where = where;
        _client = client;
        InstalledApp = binding?.InstalledApp;
    }

    public string Route { get; private set; } = "";

    public MobileOptions Options => _engine.Options;

    /// <summary>The device this worker drives: the pool's binding for its slot. Empty leaves the choice to agent-device.</summary>
    public DeviceSelection Selection => DevicePool.DeviceSelectionFor(Options.Platform, _binding);

    /// <summary>The app the build <c>appPath</c> installed, once a device provider (or the suite) has.</summary>
    public string? InstalledApp { get; set; }

    /// <summary>The app <c>app.open()</c> launches: the target's bundle id, else the app its build installed.</summary>
    public string? PinnedApp => DeviceBindings.PinnedApp(_app, InstalledApp);

    /// <summary>The build the target names, resolved against the project root.</summary>
    public string? BuildPath => _app.AppPath is null ? null : Path.GetFullPath(_app.AppPath, _projectRoot);

    /// <summary>The build the target's <c>appPath</c> names, as declared.</summary>
    public string? AppPath => _app.AppPath;

    /// <summary>
    /// Who acts through this session. A test's step never settles, and it waits out a transition. The agent reads the
    /// screen right after acting, so its actions settle first. The core session does not say who is calling, so the
    /// caller sets it. The default is the agent's.
    /// </summary>
    public ActionOrigin Origin { get; set; } = ActionOrigin.Agent;

    /// <summary>The device fixture of this session: deterministic device management for a test.</summary>
    public Device Device => _device ??= new Device(this);

    private Device? _device;

    /// <summary>The agent-device client of the live session, or <c>INVALID_STATE</c> once it closed.</summary>
    internal IDeviceClient Client => _client ?? throw DeviceSupport.InvalidState("the agent-device engine is not initialized");

    /// <summary>The session and device this worker drives, for the error messages whose recovery is per device.</summary>
    internal string Where => _where;

    /// <summary>Whether the target pins an app for <c>app.open()</c>, <c>app.restart()</c>, and <c>app.clearState()</c> to launch.</summary>
    private bool ManagesApp => _app.BundleId is not null || _app.AppPath is not null;

    internal static async Task<DeviceSession> StartAsync(MobileEngine engine, MobileStartInfo info, DeviceClientFactory createClient, CancellationToken cancellationToken)
    {
        var binding = engine.Pool.Binding(info.TargetName, info.WorkerSlot, info.Env);
        var session = engine.Pool.Session(info.TargetName, info.WorkerSlot);
        var label = DeviceBindings.DeviceLabel(binding);
        var where = $"session {session}{(label is null ? "" : $" on {label}")}";
        var started = new DeviceSession(engine, info, binding, session, where, createClient(session, binding));
        await started.BootAsync(cancellationToken).ConfigureAwait(false);
        engine.Register(started);
        return started;
    }

    /// <summary>
    /// Runs one agent-device command under a cancellation token and translates its failure. Fixture methods route
    /// through here too, so none carries its own error mapping.
    /// </summary>
    public Task<T> CommandAsync<T>(string label, Func<IDeviceClient, CancellationToken, Task<T>> run, CancellationToken cancellationToken)
    {
        var client = Client;
        return DeviceErrors.RunCommandAsync(label, token => run(client, token), cancellationToken, _where);
    }

    public Task<JsonNode?> CommandAsync(string label, string method, JsonObject? options, CancellationToken cancellationToken) =>
        CommandAsync(label, (client, token) => client.CallAsync(method, options, token), cancellationToken);

    private async Task BootAsync(CancellationToken cancellationToken)
    {
        await CommandAsync("boot", DeviceMethods.DevicesBoot, Selection.ToArgs(), cancellationToken).ConfigureAwait(false);

        // Nothing is installed here: a device provider that installed the build from appPath says so on the binding,
        // and otherwise the suite installs it where it wants to, with device.installApp().
    }

    public Task OpenAsync(string url, CancellationToken cancellationToken)
    {
        // A device app has no URL: app.open() is the fresh launch of the pinned app.
        if (DeviceLinks.IsLink(url))
        {
            return Task.FromException(DeviceSupport.Unsupported("a device app has no URL to open; app.open() launches the pinned app, and a deep link opens with device.openLink"));
        }

        return RestartAsync(cancellationToken);
    }

    /// <summary>Launches the pinned app fresh: the device's "recreate the context".</summary>
    public async Task RestartAsync(CancellationToken cancellationToken)
    {
        var app = PinnedApp ?? throw DeviceSupport.Unsupported($"launching the app needs the target's `app.bundleId`, or {UninstalledBuild}");
        await OpenAppAsync(app, new OpenAppOptions { Relaunch = true }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Clears the pinned app's persisted state and relaunches it: the device equivalent of a fresh context.</summary>
    public async Task ClearStateAsync(CancellationToken cancellationToken)
    {
        var app = PinnedApp ?? throw DeviceSupport.Unsupported($"app.clearState needs the target's `app.bundleId`, or {UninstalledBuild}");
        await CommandAsync(
            "clear app state",
            DeviceMethods.SettingsUpdate,
            DeviceArgs.Of(("setting", JsonValue.Create("clear-app-state")), ("state", JsonValue.Create("clear")), ("app", JsonValue.Create(app))),
            cancellationToken).ConfigureAwait(false);
        await OpenAppAsync(app, new OpenAppOptions { Relaunch = true }, cancellationToken).ConfigureAwait(false);
    }

    public async Task BackAsync(CancellationToken cancellationToken)
    {
        var before = _latestIndex;
        await CommandAsync("back", DeviceMethods.CommandBack, ActionSettle(), cancellationToken).ConfigureAwait(false);
        MarkAction(before);
    }

    public async Task<Observation> ObserveAsync(CancellationToken cancellationToken)
    {
        var raw = await SnapshotOrEmptyAsync(Options.Snapshot == SnapshotMode.Interactive, cancellationToken).ConfigureAwait(false);
        var projected = Project(raw);
        _generation = projected.Index.ToDictionary(entry => entry.Id, entry => Bind(entry, projected.Index));
        var viewport = await ViewportForAsync(projected, cancellationToken).ConfigureAwait(false);
        Route = DeviceSupport.ScreenLocation(raw.AppBundleId ?? raw.AppName ?? _appIdentity, SnapshotProjection.ScreenTitle(projected)) ?? "";
        return new Observation
        {
            Route = Route,
            Roots = [SnapshotProjection.ScreenRoot(projected.Roots, viewport)],
            Truncated = raw.Truncated || raw.Sparse,
        };
    }

    public Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken) =>
        _engine.Actions.PerformAsync(this, node, action, cancellationToken);

    public Task PressAsync(string key, CancellationToken cancellationToken) =>
        _engine.Actions.PressAsync(this, key, cancellationToken);

    public Task SwipeAsync(ScrollDirection direction, CancellationToken cancellationToken) =>
        _engine.Actions.SwipeAsync(this, direction, cancellationToken);

    public Task<EngineScreenshot> ScreenshotAsync(CancellationToken cancellationToken) =>
        _engine.Actions.ScreenshotAsync(this, cancellationToken);

    public ValueTask DisposeAsync() => new(CloseAsync(E2EDefaults.CleanupTimeout, CancellationToken.None));

    /// <summary>Ends the agent-device session, within a budget: a close that outlives it is abandoned, as one that fails is.</summary>
    internal async Task CloseAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var client = _client;
        _client = null;
        _engine.Unregister(this);
        _appIdentity = null;
        InstalledApp = null;
        _knownViewport = null;
        _latestIndex = null;
        _generation = [];
        if (client is null)
        {
            return;
        }

        await CleanupBudget.WithinAsync(token => CloseQuietlyAsync(client, token), timeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CloseQuietlyAsync(IDeviceClient client, CancellationToken cancellationToken)
    {
        try
        {
            await client.CallAsync(DeviceMethods.SessionsClose, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // The session is going away either way.
        }
    }

    /// <summary>
    /// Snapshot for an observation. Without a pinned app, an observation before anything is open is an empty screen
    /// rather than a fault: the model's next move is the open tool, and failing the step would take that move away.
    /// </summary>
    private async Task<RawSnapshot> SnapshotOrEmptyAsync(bool interactiveOnly, CancellationToken cancellationToken)
    {
        try
        {
            return await SnapshotAsync(interactiveOnly, cancellationToken).ConfigureAwait(false);
        }
        catch (EngineException cause) when (_client is not null && !ManagesApp && cause.Code == EngineErrorCodes.InvalidState)
        {
            return RawSnapshot.Empty;
        }
    }

    internal async Task<RawSnapshot> SnapshotAsync(bool interactiveOnly, CancellationToken cancellationToken)
    {
        var last = RawSnapshot.Empty;
        foreach (var backoff in SparseRetryBackoffMilliseconds)
        {
            if (backoff > 0)
            {
                await _engine.Delay(TimeSpan.FromMilliseconds(backoff), cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            last = await CaptureAsync(interactiveOnly, cancellationToken).ConfigureAwait(false);
            if ((last.AppBundleId ?? last.AppName) is { } identity)
            {
                _appIdentity = identity;
            }

            if (!last.Sparse)
            {
                return last;
            }
        }

        return last;
    }

    /// <summary>
    /// One capture, taken again once when the iOS runner acquired the tree but failed its own presentation check on it
    /// (a viewport it could not read, a malformed graph). The check is per capture and a capture is a read, so the
    /// retry is safe. A second failure is the runner's and propagates.
    /// </summary>
    private async Task<RawSnapshot> CaptureAsync(bool interactiveOnly, CancellationToken cancellationToken)
    {
        async Task<RawSnapshot> Take() => RawSnapshot.Read(await CommandAsync(
            "snapshot",
            DeviceMethods.CaptureSnapshot,
            DeviceArgs.Of(("interactiveOnly", JsonValue.Create(interactiveOnly))),
            cancellationToken).ConfigureAwait(false));

        try
        {
            return await Take().ConfigureAwait(false);
        }
        catch (Exception cause) when (!cancellationToken.IsCancellationRequested && DeviceErrors.IsSnapshotPresentationFailure(cause))
        {
            return await Take().ConfigureAwait(false);
        }
    }

    /// <summary>Projects a snapshot, and remembers it as the screen the session last looked at.</summary>
    private ProjectedSnapshot Project(RawSnapshot raw)
    {
        var projected = SnapshotProjection.ProjectSnapshot(raw.Nodes, () => $"n{++_idCounter}", raw.Viewport);
        _latestIndex = projected.Index;
        if (projected.Viewport is { } measured)
        {
            _knownViewport = measured;
        }

        return projected;
    }

    /// <summary>
    /// The viewport of a snapshot: its own geometry, else the last one this session learned, else the device's logical
    /// screen size read off one screenshot. A device whose size cannot be learned fails the observation: the harness
    /// measures every rect and point against the viewport, so an invented one would misplace every tap.
    /// </summary>
    private async Task<ViewportSize> ViewportForAsync(ProjectedSnapshot projected, CancellationToken cancellationToken)
    {
        if ((projected.Viewport ?? _knownViewport) is { } known)
        {
            return known;
        }

        var probed = await ProbeViewportAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new EngineException(
                EngineErrorCodes.EngineFailure,
                "the device reported no screen geometry: the snapshot has no bounds and the screenshot no logical size, so the viewport is unknown",
                retryable: false);
        _knownViewport = probed;
        return probed;
    }

    /// <summary>The device's logical screen size as its screenshot reports it. Null when it reports none.</summary>
    private Task<ViewportSize?> ProbeViewportAsync(CancellationToken cancellationToken) =>
        CaptureScreenshotAsync((shot, _) => Task.FromResult(DeviceSupport.LogicalScreenSize(shot)), normalizeStatusBar: false, cancellationToken);

    private const string UninstalledBuild = "the build the target's `app.appPath` names installed first with `device.installApp()`";

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>A JSON number, whatever type backs it: one parsed from the daemon's text and one built in code differ.</summary>
    private static double? Number(JsonNode? node) =>
        node is JsonValue && JsonSerializer.SerializeToElement(node) is { ValueKind: JsonValueKind.Number } element ? element.GetDouble() : null;

    /// <summary>The snapshot fields this engine reads off agent-device's response.</summary>
    internal sealed record RawSnapshot(
        IReadOnlyList<RawNode> Nodes,
        bool Truncated,
        string? AppName,
        string? AppBundleId,
        bool Sparse,
        ViewportSize? Viewport = null,
        string? KeyboardKind = null)
    {
        public static RawSnapshot Empty { get; } = new([], false, null, null, false);

        public static RawSnapshot Read(JsonNode? response)
        {
            if (response is not JsonObject data)
            {
                return Empty;
            }

            IReadOnlyList<RawNode> nodes;
            try
            {
                nodes = data["nodes"] is JsonArray array ? array.Deserialize<List<RawNode>>(NodeJson) ?? [] : [];
            }
            catch (JsonException cause)
            {
                throw new EngineException(EngineErrorCodes.EngineFailure, $"the device returned a snapshot this engine cannot read: {cause.Message}", retryable: false, cause);
            }

            ViewportSize? viewport = Number(data["viewport"]?["width"]) is > 0 and var width && Number(data["viewport"]?["height"]) is > 0 and var height
                ? new ViewportSize(width, height)
                : null;
            return new RawSnapshot(
                nodes,
                data["truncated"] is JsonValue truncated && truncated.TryGetValue<bool>(out var cut) && cut,
                Text(data["appName"]),
                Text(data["appBundleId"]),
                Text(data["snapshotQuality"]?["state"]) == "sparse",
                viewport,
                Text(data["keyboard"]?["kind"]));
        }
    }
}
