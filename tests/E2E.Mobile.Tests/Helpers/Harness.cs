// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace E2E.Mobile.Tests;

/// <summary>
/// The device engine over a scripted client, for unit tests: a harness records the sessions and connections each
/// client was minted for, carries the target app the runner would hand prepare, and <see cref="Boot"/> starts the
/// worker of one slot.
/// </summary>
internal sealed class Harness
{
    /// <summary>Deliberately not the working directory: relative build paths must resolve here, not there.</summary>
    public static readonly string ProjectRoot = Path.GetFullPath("/project");

    private Harness(MobileEngine engine, FakeDeviceClient fake, List<string> sessions, List<DeviceConnection?> connections, List<TimeSpan> delays, MobileApp app)
    {
        Engine = engine;
        Fake = fake;
        Sessions = sessions;
        Connections = connections;
        Delays = delays;
        App = app;
    }

    public MobileEngine Engine { get; }

    public FakeDeviceClient Fake { get; }

    public List<string> Sessions { get; }

    /// <summary>The connection each client was minted with. Null is the local daemon with defaults.</summary>
    public List<DeviceConnection?> Connections { get; }

    /// <summary>The delays the engine asked for between snapshot retries.</summary>
    public List<TimeSpan> Delays { get; }

    /// <summary>The target's app, as the runner hands it to prepare.</summary>
    public MobileApp App { get; }

    /// <summary>An engine over the scripted client. Unpinned leaves the target's bundle id out.</summary>
    public static Harness Create(Func<MobileOptions, MobileOptions>? configure = null, bool pinned = true, Func<MobileApp, MobileApp>? app = null)
    {
        var fake = new FakeDeviceClient();
        fake.Respond(DeviceMethods.CaptureSnapshot, _ => FakeDeviceClient.SettingsSnapshot());
        fake.Respond(DeviceMethods.AppsOpen, _ => new JsonObject { ["session"] = "s", ["appName"] = "Settings", ["appBundleId"] = "com.apple.Preferences" });

        // The viewport probe's answer for a tree without geometry. Tests that need more script it.
        fake.Respond(DeviceMethods.CaptureScreenshot, _ => new JsonObject { ["logicalWidth"] = 390, ["logicalHeight"] = 844 });
        var sessions = new List<string>();
        var connections = new List<DeviceConnection?>();
        var delays = new List<TimeSpan>();
        var appInfo = (app ?? (current => current))(new MobileApp { BundleId = pinned ? "Settings" : null });
        var options = new MobileOptions { Platform = MobilePlatform.Ios, TargetName = "ios-simulator", App = appInfo };
        options = configure?.Invoke(options) ?? options;
        var engine = new MobileEngine(
            options,
            (session, connection) =>
            {
                sessions.Add(session);
                connections.Add(connection);
                return fake;
            })
        {
            Delay = (span, _) =>
            {
                delays.Add(span);
                return Task.CompletedTask;
            },
        };
        return new Harness(engine, fake, sessions, connections, delays, options.App);
    }

    /// <summary>Starts the worker of one slot, as its init does, with the environment the runner left.</summary>
    public Task<DeviceSession> Boot(string targetName = "ios-simulator", int workerSlot = 0, IReadOnlyDictionary<string, string?>? env = null) =>
        Engine.StartAsync(
            new MobileStartInfo { RunId = "run-1", TargetName = targetName, WorkerSlot = workerSlot, ProjectRoot = ProjectRoot, Env = env ?? new Dictionary<string, string?>() },
            CancellationToken.None);

    /// <summary>The info the runner hands prepare.</summary>
    public MobilePrepareInfo PrepareInfo(int slots = 1, string targetName = "ios", Action<string>? log = null, IReadOnlyDictionary<string, string?>? env = null) => new()
    {
        RunId = "run-1",
        TargetName = targetName,
        ProjectRoot = ProjectRoot,
        Slots = slots,
        Env = env ?? new Dictionary<string, string?>(),
        App = App,
        Log = log ?? (_ => { }),
    };

    public static MobileFinishInfo FinishInfo(Action<string>? log = null, string targetName = "ios") => new()
    {
        RunId = "run-1",
        TargetName = targetName,
        Env = new Dictionary<string, string?>(),
        Log = log ?? (_ => { }),
        CleanupTimeout = TimeSpan.FromSeconds(5),
    };

    /// <summary>The environment a worker is started with from what <c>prepare</c> returned.</summary>
    public static IReadOnlyDictionary<string, string?> WorkerEnv(MobilePrepareResult result) =>
        result.Env.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);

    /// <summary>The pool variable <c>prepare</c> wrote for a target, found by its readable prefix (the suffix is a digest of the name).</summary>
    public static string PoolVariableIn(IReadOnlyDictionary<string, string> env, string readable)
    {
        var pattern = new Regex($"^E2E_AGENT_DEVICE_POOL_{readable}_[0-9A-F]{{8}}$");
        return env.Keys.FirstOrDefault(key => pattern.IsMatch(key)) ?? throw new InvalidOperationException($"no pool variable for {readable} in {string.Join(", ", env.Keys)}");
    }
}
