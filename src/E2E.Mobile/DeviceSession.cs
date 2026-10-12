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
/// (<see cref="SnapshotProjection.RootId"/>), and its node refs are valid for that observation only.
/// </summary>
public sealed class DeviceSession : IEngineSession
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

    /// <summary>The projection of the screen the last observation read. Refs of its nodes are the ones the session hands out.</summary>
    internal ProjectedSnapshot? Latest { get; private set; }

    internal static async Task<DeviceSession> StartAsync(MobileEngine engine, MobileStartInfo info, DeviceClientFactory createClient, CancellationToken cancellationToken)
    {
        var binding = engine.Pool.Binding(info.TargetName, info.WorkerSlot, info.Env);
        var session = engine.Pool.Session(info.TargetName, info.WorkerSlot);
        var label = DeviceBindings.DeviceLabel(binding);
        var where = $"session {session}{(label is null ? "" : $" on {label}")}";
        var started = new DeviceSession(engine, info, binding, session, where, createClient(session, binding));
        await started.BootAsync(cancellationToken).ConfigureAwait(false);
        return started;
    }

    /// <summary>
    /// Runs one agent-device command under a cancellation token and translates its failure. Fixture methods route
    /// through here too, so none carries its own error mapping.
    /// </summary>
    public Task<T> CommandAsync<T>(string label, Func<IDeviceClient, CancellationToken, Task<T>> run, CancellationToken cancellationToken)
    {
        var client = _client ?? throw DeviceSupport.InvalidState("the agent-device engine is not initialized");
        return DeviceErrors.RunCommandAsync(label, token => run(client, token), cancellationToken, _where);
    }

    public Task<JsonNode?> CommandAsync(string label, string method, JsonObject? options, CancellationToken cancellationToken) =>
        CommandAsync(label, (client, token) => client.CallAsync(method, options, token), cancellationToken);

    private async Task BootAsync(CancellationToken cancellationToken)
    {
        await CommandAsync("boot", DeviceMethods.DevicesBoot, Selection.ToArgs(), cancellationToken).ConfigureAwait(false);
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
        await OpenAppAsync(app, relaunch: true, cancellationToken).ConfigureAwait(false);
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
        await OpenAppAsync(app, relaunch: true, cancellationToken).ConfigureAwait(false);
    }

    public async Task BackAsync(CancellationToken cancellationToken)
    {
        var settle = Options.Settle ? DeviceArgs.Of(("settle", JsonValue.Create(true)), ("settleQuietMs", JsonValue.Create(Options.SettleMilliseconds))) : new JsonObject();
        await CommandAsync("back", DeviceMethods.CommandBack, settle, cancellationToken).ConfigureAwait(false);
    }

    public async Task<Observation> ObserveAsync(CancellationToken cancellationToken)
    {
        var raw = await SnapshotOrEmptyAsync(Options.Snapshot == SnapshotMode.Interactive, cancellationToken).ConfigureAwait(false);
        var projected = SnapshotProjection.ProjectSnapshot(raw.Nodes, () => $"n{++_idCounter}", raw.Viewport);
        Latest = projected;
        if (projected.Viewport is { } measured)
        {
            _knownViewport = measured;
        }

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
        _engine.Actions?.PerformAsync(this, node, action, cancellationToken) ?? Unsupported("node actions");

    public Task PressAsync(string key, CancellationToken cancellationToken) =>
        _engine.Actions?.PressAsync(this, key, cancellationToken) ?? Unsupported("pressing a key");

    public Task SwipeAsync(ScrollDirection direction, CancellationToken cancellationToken) =>
        _engine.Actions?.SwipeAsync(this, direction, cancellationToken) ?? Unsupported("scrolling");

    public Task<EngineScreenshot> ScreenshotAsync(CancellationToken cancellationToken) =>
        _engine.Actions?.ScreenshotAsync(this, cancellationToken)
        ?? Task.FromException<EngineScreenshot>(DeviceSupport.Unsupported("screenshots are not available on a device yet"));

    public ValueTask DisposeAsync() => new(CloseAsync(E2EDefaults.CleanupTimeout, CancellationToken.None));

    /// <summary>Ends the agent-device session, within a budget: a close that outlives it is abandoned, as one that fails is.</summary>
    internal async Task CloseAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var client = _client;
        _client = null;
        _appIdentity = null;
        InstalledApp = null;
        _knownViewport = null;
        Latest = null;
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
    /// Opens an app in the session. The engine's own launch options belong to a fresh launch of the pinned app: a
    /// foreground-only open of a running app takes no arguments, and a permission change there would terminate the
    /// app it means to keep.
    /// </summary>
    private async Task OpenAppAsync(string app, bool relaunch, CancellationToken cancellationToken)
    {
        var configured = relaunch && app == PinnedApp;
        if (configured && _app.Permissions is { } permissions)
        {
            await PresetPermissionsAsync(app, permissions, cancellationToken).ConfigureAwait(false);
        }

        await LaunchAsync(app, relaunch, configured ? _app.LaunchArguments : null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>One <c>open</c> of an app.</summary>
    private async Task LaunchAsync(string app, bool relaunch, IReadOnlyList<string>? launchArguments, CancellationToken cancellationToken)
    {
        var result = await CommandAsync(
            $"open {app}",
            DeviceMethods.AppsOpen,
            Selection.ToArgs(
                ("app", JsonValue.Create(app)),
                ("relaunch", relaunch ? JsonValue.Create(true) : null),
                ("launchArgs", launchArguments is { Count: > 0 } ? new JsonArray([.. launchArguments.Select(argument => (JsonNode?)JsonValue.Create(argument))]) : null)),
            cancellationToken).ConfigureAwait(false);
        _appIdentity = Text(result?["appBundleId"]) ?? Text(result?["appName"]) ?? app;
        Latest = null;
    }

    /// <summary>
    /// Puts an app's permissions in place before it launches, never after: iOS terminates a running app whose
    /// permission changed. Each change names the app, so it needs no open in the session first.
    /// </summary>
    private async Task PresetPermissionsAsync(string app, IReadOnlyDictionary<DevicePermission, PermissionState> permissions, CancellationToken cancellationToken)
    {
        foreach (var (permission, state) in permissions)
        {
            await CommandAsync(
                $"permission {permission.WireName()} {state.WireName()}",
                DeviceMethods.SettingsUpdate,
                Selection.ToArgs(
                    ("setting", JsonValue.Create("permission")),
                    ("permission", JsonValue.Create(permission.WireName())),
                    ("state", JsonValue.Create(state.WireName())),
                    ("app", JsonValue.Create(app))),
                cancellationToken).ConfigureAwait(false);
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
        catch (EngineException cause) when (_client is not null && PinnedApp is null && cause.Code == EngineErrorCodes.InvalidState)
        {
            return new RawSnapshot([], false, null, null, false);
        }
    }

    private async Task<RawSnapshot> SnapshotAsync(bool interactiveOnly, CancellationToken cancellationToken)
    {
        var last = new RawSnapshot([], false, null, null, false);
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
    private async Task<ViewportSize?> ProbeViewportAsync(CancellationToken cancellationToken)
    {
        var directory = Directory.CreateTempSubdirectory("e2e-agent-device-");
        try
        {
            var shot = await CommandAsync(
                "screenshot",
                DeviceMethods.CaptureScreenshot,
                DeviceArgs.Of(("path", JsonValue.Create(Path.Combine(directory.FullName, "screenshot.png")))),
                cancellationToken).ConfigureAwait(false);
            return DeviceSupport.LogicalScreenSize(new RawScreenshotResult
            {
                Path = Text(shot?["path"]),
                Width = Number(shot?["width"]),
                Height = Number(shot?["height"]),
                LogicalWidth = Number(shot?["logicalWidth"]),
                LogicalHeight = Number(shot?["logicalHeight"]),
                PixelDensity = Number(shot?["pixelDensity"]),
            });
        }
        finally
        {
            try
            {
                directory.Delete(recursive: true);
            }
            catch (IOException)
            {
                // The probe is best effort and so is its cleanup.
            }
        }
    }

    private const string UninstalledBuild = "the build the target's `app.appPath` names installed first with `device.installApp()`";

    private static Task Unsupported(string what) =>
        Task.FromException(DeviceSupport.Unsupported($"{what} are not available on a device yet"));

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

    /// <summary>A JSON number, whatever type backs it: one parsed from the daemon's text and one built in code differ.</summary>
    private static double? Number(JsonNode? node) =>
        node is JsonValue && JsonSerializer.SerializeToElement(node) is { ValueKind: JsonValueKind.Number } element ? element.GetDouble() : null;

    /// <summary>The snapshot fields this engine reads off agent-device's response.</summary>
    private sealed record RawSnapshot(IReadOnlyList<RawNode> Nodes, bool Truncated, string? AppName, string? AppBundleId, bool Sparse, ViewportSize? Viewport = null)
    {
        /// <summary>The viewport agent-device measured, when it is a usable size: the same bar <c>ViewportOf</c> sets for a tree.</summary>
        private static ViewportSize? PublishedViewport(JsonObject? viewport) =>
            Number(viewport?["width"]) is > 0 and var width && Number(viewport?["height"]) is > 0 and var height ? new ViewportSize(width, height) : null;

        public static RawSnapshot Read(JsonNode? response)
        {
            if (response is not JsonObject data)
            {
                return new RawSnapshot([], false, null, null, false);
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

            return new RawSnapshot(
                nodes,
                data["truncated"] is JsonValue truncated && truncated.TryGetValue<bool>(out var cut) && cut,
                Text(data["appName"]),
                Text(data["appBundleId"]),
                Text(data["snapshotQuality"]?["state"]) == "sparse",
                PublishedViewport(data["viewport"] as JsonObject));
        }
    }
}
