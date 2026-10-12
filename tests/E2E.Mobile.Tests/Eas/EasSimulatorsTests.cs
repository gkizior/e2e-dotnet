// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Mobile.Eas;

namespace E2E.Mobile.Tests.Eas;

/// <summary>
/// <see cref="EasSimulators"/> against a fake Expo GraphQL API: the create input, polling to ready and the queue line, the
/// lease and its log lines, stopping on release and after a failed or cancelled start, the token or eas-cli login, the
/// project id from the app config, and the target's app path.
/// </summary>
public sealed class EasSimulatorsTests : IDisposable
{
    private static readonly string[] OneTag = ["nightly"];

    private static int _runs;

    private readonly List<string> _directories = [];
    private readonly FakeEas _eas = new();
    private readonly string _emptyHome;
    private readonly string _emptyProject;

    public EasSimulatorsTests()
    {
        _emptyHome = Temp("e2e-eas-home-");
        _emptyProject = Temp("e2e-eas-project-");
    }

    public void Dispose()
    {
        foreach (var directory in _directories)
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static readonly string Login = new JsonObject
    {
        ["auth"] = new JsonObject { ["sessionSecret"] = "session-secret", ["userId"] = "u1", ["username"] = "ada", ["currentConnection"] = "Browser-Flow-Authentication" },
    }.ToJsonString();

    private static string Linked(string projectId) =>
        new JsonObject { ["expo"] = new JsonObject { ["name"] = "shop", ["extra"] = new JsonObject { ["eas"] = new JsonObject { ["projectId"] = projectId } } } }.ToJsonString();

    private const string DefaultExpoScript = "process.stdout.write(JSON.stringify({ name: 'shop', extra: { eas: { projectId: process.env.SHOP_PROJECT_ID } } }));";

    private string Temp(string prefix)
    {
        var path = Directory.CreateTempSubdirectory(prefix).FullName;
        _directories.Add(path);
        return path;
    }

    /// <summary>A home where eas-cli keeps <c>state</c> under <paramref name="directory"/>, as <c>eas login</c> writes it.</summary>
    private string HomeWith(string state, string directory = ".expo")
    {
        var path = Temp("e2e-eas-home-");
        Directory.CreateDirectory(Path.Combine(path, directory));
        File.WriteAllText(Path.Combine(path, directory, "state.json"), state);
        return path;
    }

    /// <summary>The run's home as the provider reads it on every platform: <c>HOME</c>, or <c>USERPROFILE</c> on Windows.</summary>
    private static Dictionary<string, string?> Home(string path, string? token = null)
    {
        var env = new Dictionary<string, string?> { ["HOME"] = path, ["USERPROFILE"] = path };
        if (token is not null)
        {
            env["EXPO_TOKEN"] = token;
        }

        return env;
    }

    /// <summary>An e2e project root holding <paramref name="files"/>, each path relative to it; removed with the homes.</summary>
    private string ProjectWith(Dictionary<string, string>? files = null)
    {
        var root = Temp("e2e-eas-project-");
        foreach (var (path, text) in files ?? [])
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
            File.WriteAllText(Path.Combine(root, path), text);
        }

        return root;
    }

    /// <summary>
    /// A stand-in for the project's <c>expo</c> package whose <c>expo config</c> logs its arguments and the
    /// <c>EXPO_NO_DOTENV</c> and <c>CI</c> it got to <c>expo-calls.txt</c>, then runs <paramref name="script"/>: by default it
    /// prints a config with <c>SHOP_PROJECT_ID</c> from its environment as the project id.
    /// </summary>
    private static Dictionary<string, string> FakeExpo(string script = DefaultExpoScript) => new()
    {
        ["node_modules/expo/package.json"] = """{ "name": "expo", "version": "0.0.0" }""",
        ["node_modules/expo/bin/cli"] = "require('node:fs').appendFileSync('expo-calls.txt', [...process.argv.slice(2), 'EXPO_NO_DOTENV=' + process.env.EXPO_NO_DOTENV, 'CI=' + process.env.CI].join(' ') + '\\n');\n" + script + "\n",
    };

    private static Dictionary<string, string> Merge(Dictionary<string, string> first, Dictionary<string, string> second)
    {
        foreach (var (name, text) in second)
        {
            first[name] = text;
        }

        return first;
    }

    private Dictionary<string, string?> Env => Home(_emptyHome, "expo-test");

    private DeviceRequest Request(Func<DeviceRequest, DeviceRequest>? with = null)
    {
        var request = new DeviceRequest
        {
            Platform = MobilePlatform.Ios,
            RunId = $"run-{Interlocked.Increment(ref _runs)}",
            TargetName = "ios",
            Slot = 0,
            Slots = 2,
            App = "com.example.app",
            AgentDeviceVersion = "0.21.18",
            ProjectRoot = _emptyProject,
            Env = Env,
            Log = _ => { },
        };
        return with?.Invoke(request) ?? request;
    }

    private EasSimulators Provider(EasSimulatorsOptions? options = null, long step = 0) =>
        new(options ?? new EasSimulatorsOptions { ProjectId = "p1" }, _eas.Client, new FakeClock { Step = step }.Clock);

    private static DeviceReleaseContext ReleaseContext(string runId, IReadOnlyDictionary<string, string?> env) =>
        new() { RunId = runId, TargetName = "ios", Env = env, Log = _ => { } };

    private DeviceReleaseContext ReleaseContext(string runId) => ReleaseContext(runId, Env);

    private static readonly (string Message, string Code) NotFound = ("Entity Not Found", "NOT_FOUND_ERROR");

    private static void AssertJson(string expected, JsonNode? actual) =>
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(expected), actual), $"expected {expected}, got {actual?.ToJsonString()}");

    [Fact]
    public async Task Starts_an_agent_device_session_named_after_the_slot_tagged_with_the_run_on_the_engines_agent_device_with_the_idle_backstop()
    {
        var request = Request(r => r with { Slot = 1, RunId = "run-1" });
        await Provider(new EasSimulatorsOptions { ProjectId = "p1", BuildId = "b1", Device = "iPhone 17 Pro", Tags = OneTag }).AcquireAsync(request, default);
        var call = _eas.Calls[0];
        Assert.Equal(EasOperation.Create, call.Operation);
        Assert.Equal("Bearer expo-test", call.Authorization);
        AssertJson(
            """
            {
              "appId": "p1",
              "platform": "IOS",
              "type": "AGENT_DEVICE",
              "name": "e2e ios 2 of 2",
              "tags": ["nightly", "e2e", "e2e-run:run-1", "e2e-target:ios"],
              "buildId": "b1",
              "ios": { "deviceIdentifier": "iPhone 17 Pro" },
              "maxIdleTimeMinutes": 10,
              "packageVersion": "0.21.18"
            }
            """,
            call.Variables["input"]);
    }

    [Fact]
    public async Task Names_an_Android_emulator_under_android_and_passes_an_archive_URL_idle_time_and_duration_through()
    {
        await Provider(new EasSimulatorsOptions
        {
            ProjectId = "p1",
            ApplicationArchiveUrl = "https://example.com/app.apk",
            Device = "pixel_9",
            MaxIdleTimeMinutes = 30,
            MaxDurationMinutes = 60,
        }).AcquireAsync(Request(r => r with { Platform = MobilePlatform.Android }), default);
        var input = _eas.Create(0);
        Assert.Equal("ANDROID", input["platform"]!.GetValue<string>());
        AssertJson("""{ "deviceIdentifier": "pixel_9" }""", input["android"]);
        Assert.Equal("https://example.com/app.apk", input["applicationArchiveUrl"]!.GetValue<string>());
        Assert.Equal(30, input["maxIdleTimeMinutes"]!.GetValue<int>());
        Assert.Equal(60, input["maxRunTimeMinutes"]!.GetValue<int>());
        Assert.False(input.ContainsKey("ios"));
    }

    [Fact]
    public async Task Leases_a_session_whose_queue_was_brief_without_a_queue_line()
    {
        _eas.States = [FakeEas.Session("NEW", "NEW"), FakeEas.Session("NEW", "IN_PROGRESS"), (JsonObject)FakeEas.Ready.DeepClone()];
        var lines = new List<string>();
        await Provider().AcquireAsync(Request(r => r with { Log = lines.Add }), default);
        Assert.Equal([$"simulator session s1, {FakeEas.SessionUrl}", "watch at https://expo.dev/simulator-preview/s1"], lines);
    }

    [Fact]
    public async Task Leaves_a_preview_that_needs_a_token_out_of_the_log()
    {
        var ready = (JsonObject)FakeEas.Ready.DeepClone();
        ready["remoteConfig"]!["webPreviewToken"] = "preview-secret";
        _eas.States = [ready];
        var lines = new List<string>();
        await Provider().AcquireAsync(Request(r => r with { Log = lines.Add }), default);
        Assert.Equal([$"simulator session s1, {FakeEas.SessionUrl}"], lines);
    }

    [Fact]
    public async Task Polls_until_the_daemon_is_reachable_and_leases_it_saying_once_that_it_has_waited_in_the_queue_for_two_minutes()
    {
        var queued = FakeEas.Queued();
        _eas.States = [(JsonObject)queued.DeepClone(), (JsonObject)queued.DeepClone(), (JsonObject)queued.DeepClone(), (JsonObject)queued.DeepClone(), FakeEas.Session("NEW", "IN_PROGRESS"), (JsonObject)FakeEas.Ready.DeepClone()];
        var lines = new List<string>();
        var lease = await Provider(step: 50_000).AcquireAsync(Request(r => r with { Log = lines.Add }), default);
        Assert.Equal("s1", lease.Id);
        Assert.Equal(new DeviceDaemon("https://agent-device-s1.eas-simulator.ngrok.dev", "daemon-token"), lease.Daemon);
        Assert.Equal(["create", "state", "state", "state", "state", "state", "state"], _eas.Operations);
        AssertJson("""{ "id": "s1" }""", _eas.Calls[1].Variables);
        Assert.Equal(
            [
                $"simulator session s1, {FakeEas.SessionUrl}",
                "still queued at EAS after two minutes; tests start once every worker has a simulator",
                "watch at https://expo.dev/simulator-preview/s1",
            ],
            lines);
    }

    [Fact]
    public async Task Stops_the_session_on_release_and_one_EAS_no_longer_knows_counts_as_stopped()
    {
        var provider = Provider();
        var request = Request();
        var lease = await provider.AcquireAsync(request, default);
        await provider.ReleaseAsync(lease, ReleaseContext(request.RunId), default);
        var stop = _eas.Calls[^1];
        Assert.Equal((EasOperation.Stop, "Bearer expo-test"), (stop.Operation, stop.Authorization));
        AssertJson("""{ "id": "s1" }""", stop.Variables);
        _eas.Errors[EasOperation.Stop] = NotFound;
        await provider.ReleaseAsync(lease, ReleaseContext(request.RunId), default);
        _eas.Errors[EasOperation.Stop] = ("Internal error", "INTERNAL_SERVER_ERROR");
        var failure = await Assert.ThrowsAsync<EasException>(() => provider.ReleaseAsync(lease, ReleaseContext(request.RunId), default));
        Assert.Equal("EAS: Internal error", failure.Message);
    }

    [Fact]
    public async Task Cancels_the_job_of_a_session_stopped_while_queued_which_would_otherwise_start_later_and_hold_a_concurrent_session()
    {
        var provider = Provider();
        var request = Request();
        var lease = await provider.AcquireAsync(request, default);
        _eas.StopJob = ("j1", "IN_QUEUE");
        await provider.ReleaseAsync(lease, ReleaseContext(request.RunId), default);
        Assert.Equal(["stop", "cancel"], _eas.Operations[^2..]);
        AssertJson("""{ "id": "j1" }""", _eas.Calls[^1].Variables);
        _eas.Calls = [];
        _eas.StopJob = ("j1", "FINISHED");
        await provider.ReleaseAsync(lease, ReleaseContext(request.RunId), default);
        Assert.Equal(["stop"], _eas.Operations);
    }

    [Fact]
    public async Task Stops_a_session_that_ends_before_it_is_ready_and_names_it()
    {
        _eas.States = [FakeEas.Session("NEW", "ERRORED")];
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().AcquireAsync(Request(), default));
        Assert.Contains($"simulator session s1 did not become ready: session job errored; stopped it, {FakeEas.SessionUrl}", failure.Message, StringComparison.Ordinal);
        Assert.Equal(EasOperation.Stop, _eas.Calls[^1].Operation);

        var finished = (JsonObject)FakeEas.Ready.DeepClone();
        finished["turtleJobRun"] = new JsonObject { ["status"] = "FINISHED" };
        _eas.States = [finished];
        failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().AcquireAsync(Request(), default));
        Assert.Contains("did not become ready: session job finished; stopped it", failure.Message, StringComparison.Ordinal);

        _eas.States = [FakeEas.Session("ERRORED", null)];
        failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().AcquireAsync(Request(), default));
        Assert.Contains("did not become ready: session errored; stopped it", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Starts_no_session_for_an_already_cancelled_request()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Provider().AcquireAsync(Request(), cancelled.Token));
        Assert.Empty(_eas.Calls);
    }

    [Fact]
    public async Task Starts_no_session_when_cancellation_arrives_during_prerequisite_resolution()
    {
        // The prerequisite is the project id, read from a dynamic app config by the project's `expo config`.
        var marker = Path.Combine(Temp("e2e-eas-marker-"), "started");
        var projectRoot = ProjectWith(Merge(
            new() { ["app.config.ts"] = "export default {};" },
            FakeExpo($"require('node:fs').writeFileSync({JsonValue.Create(marker)!.ToJsonString()}, ''); setTimeout(() => {{}}, 60000);")));
        using var cancel = new CancellationTokenSource();
        var acquiring = Provider(new EasSimulatorsOptions()).AcquireAsync(Request(r => r with { ProjectRoot = projectRoot }), cancel.Token);
        await WaitForAsync(marker);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquiring);
        Assert.Empty(_eas.Calls);
    }

    [Fact]
    public async Task Stops_a_session_whose_request_was_cancelled_while_it_started_and_says_when_stopping_failed_too()
    {
        using var cancel = new CancellationTokenSource();
        _eas.States = [FakeEas.Queued()];
        _eas.OnState = () => cancel.Cancel();
        _eas.Errors[EasOperation.Stop] = ("Service unavailable", "INTERNAL_SERVER_ERROR");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().AcquireAsync(Request(), cancel.Token));
        Assert.Contains(
            $"simulator session s1 did not become ready: cancelled; stopping it failed, so EAS stops it at its idle or duration limit (EAS: Service unavailable), {FakeEas.SessionUrl}",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(EasOperation.Stop, _eas.Calls[^1].Operation);
    }

    [Fact]
    public async Task Gives_up_on_a_session_still_queued_once_another_session_of_the_run_from_any_target_has_idled_to_the_limit()
    {
        var clock = new FakeClock { Step = 60_000 }.Clock;
        var ios = Request(r => r with { RunId = "run-idle" });
        var iosProvider = new EasSimulators(new EasSimulatorsOptions { ProjectId = "p1", MaxIdleTimeMinutes = 5 }, _eas.Client, clock);
        var lease = await iosProvider.AcquireAsync(ios, default);
        _eas.States = [FakeEas.Queued()];
        var android = new EasSimulators(new EasSimulatorsOptions { ProjectId = "p1", MaxIdleTimeMinutes = 5 }, _eas.Client, clock);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => android.AcquireAsync(
            Request(r => r with { RunId = "run-idle", Platform = MobilePlatform.Android, TargetName = "android", Slots = 1 }),
            default));
        Assert.Contains(
            "simulator session s1 did not become ready: still queued while another session of this run idled to its `maxIdleTimeMinutes`, where EAS stops it; lower `workers` or raise `maxIdleTimeMinutes`; stopped it",
            failure.Message,
            StringComparison.Ordinal);
        Assert.Equal(EasOperation.Stop, _eas.Calls[^1].Operation);
        await iosProvider.ReleaseAsync(lease, ReleaseContext("run-idle"), default);
    }

    [Fact]
    public async Task Measures_a_ready_session_by_its_own_idle_limit_not_the_queued_ones()
    {
        var clock = new FakeClock { Step = 60_000 }.Clock;
        var patient = new EasSimulators(new EasSimulatorsOptions { ProjectId = "p1", MaxIdleTimeMinutes = 30 }, _eas.Client, clock);
        var lease = await patient.AcquireAsync(Request(r => r with { RunId = "run-limits" }), default);
        _eas.States = [.. Enumerable.Range(0, 10).Select(_ => FakeEas.Queued()), (JsonObject)FakeEas.Ready.DeepClone()];
        var impatient = new EasSimulators(new EasSimulatorsOptions { ProjectId = "p1", MaxIdleTimeMinutes = 2 }, _eas.Client, clock);
        Assert.Equal("s1", (await impatient.AcquireAsync(Request(r => r with { RunId = "run-limits" }), default)).Id);
        await patient.ReleaseAsync(lease, ReleaseContext("run-limits"), default);
    }

    [Fact]
    public async Task Keeps_waiting_however_long_it_queues_while_no_session_of_the_run_is_ready_or_after_they_were_released()
    {
        var clock = new FakeClock { Step = 60_000 }.Clock;
        EasSimulators Make() => new(new EasSimulatorsOptions { ProjectId = "p1", MaxIdleTimeMinutes = 5 }, _eas.Client, clock);
        _eas.States = [.. Enumerable.Range(0, 20).Select(_ => FakeEas.Queued()), (JsonObject)FakeEas.Ready.DeepClone()];
        Assert.Equal("s1", (await Make().AcquireAsync(Request(), default)).Id);

        var provider = Make();
        _eas.States = [(JsonObject)FakeEas.Ready.DeepClone()];
        var lease = await provider.AcquireAsync(Request(r => r with { RunId = "run-released" }), default);
        await provider.ReleaseAsync(lease, ReleaseContext("run-released"), default);
        _eas.States = [.. Enumerable.Range(0, 20).Select(_ => FakeEas.Queued()), (JsonObject)FakeEas.Ready.DeepClone()];
        Assert.Equal("s1", (await provider.AcquireAsync(Request(r => r with { RunId = "run-released" }), default)).Id);
    }

    [Fact]
    public async Task Keeps_the_idle_limit_below_a_short_duration_as_EAS_requires_and_pins_the_agent_device_an_option_names()
    {
        await Provider(new EasSimulatorsOptions { ProjectId = "p1", MaxDurationMinutes = 5, AgentDeviceVersion = "0.22.0" }).AcquireAsync(Request(), default);
        var input = _eas.Create(0);
        Assert.Equal((5, 4, "0.22.0"), (input["maxRunTimeMinutes"]!.GetValue<int>(), input["maxIdleTimeMinutes"]!.GetValue<int>(), input["packageVersion"]!.GetValue<string>()));
        _eas.Calls = [];
        await Provider(new EasSimulatorsOptions { ProjectId = "p1", MaxDurationMinutes = 1 }).AcquireAsync(Request(), default);
        Assert.False(_eas.Create(0).ContainsKey("maxIdleTimeMinutes"));
        var invalid = Assert.Throws<ConfigurationException>(() => Provider(new EasSimulatorsOptions { ProjectId = "p1", MaxIdleTimeMinutes = 10, MaxDurationMinutes = 10 }));
        Assert.Equal("INVALID_CONFIG", invalid.Code);
        Assert.Contains("`maxIdleTimeMinutes` must be below `maxDurationMinutes`", invalid.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Gives_up_on_a_session_that_boots_for_fifteen_minutes_after_leaving_the_queue()
    {
        _eas.States = [FakeEas.Queued(), FakeEas.Session("NEW", "IN_PROGRESS")];
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider(step: 60_000).AcquireAsync(Request(), default));
        Assert.Contains("did not become ready: still starting after 15 minutes; stopped it", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rides_out_a_few_failed_polls_and_gives_up_after_more_in_a_row()
    {
        _eas.StateOutages = 3;
        Assert.Equal("s1", (await Provider().AcquireAsync(Request(), default)).Id);
        _eas.StateOutages = 4;
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().AcquireAsync(Request(), default));
        Assert.Contains("did not become ready: EAS: HTTP 502, Bad gateway; stopped it", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Stops_a_session_EAS_created_while_the_request_was_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        _eas.OnCreate = () => cancel.Cancel();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().AcquireAsync(Request(), cancel.Token));
        Assert.Contains("simulator session s1 did not become ready: cancelled; stopped it", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["create", "state", "stop"], _eas.Operations);
    }

    [Fact]
    public async Task Surfaces_an_EAS_error_with_its_message()
    {
        _eas.Errors[EasOperation.Create] = ("Device run sessions are not enabled for this account", "FORBIDDEN");
        var failure = await Assert.ThrowsAsync<EasException>(() => Provider().AcquireAsync(Request(), default));
        Assert.Equal("EAS: Device run sessions are not enabled for this account", failure.Message);
    }

    [Fact]
    public async Task Names_a_response_that_is_not_GraphQL_by_its_status()
    {
        _eas.StateOutages = 0;
        var provider = new EasSimulators(
            new EasSimulatorsOptions { ProjectId = "p1" },
            new HttpClient(new Bad()),
            new FakeClock().Clock);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.AcquireAsync(Request(), default));
        Assert.Equal("EAS: HTTP 502, Bad gateway", failure.Message);
    }

    [Fact]
    public async Task Fails_without_EXPO_TOKEN_or_an_eas_cli_login()
    {
        var blank = Home(_emptyHome, " ");
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().AcquireAsync(Request(r => r with { Env = blank }), default));
        Assert.Equal("EXPO_TOKEN is not set and eas-cli is not logged in; run `eas login` or set EXPO_TOKEN", failure.Message);
        var loggedOut = Home(HomeWith("""{ "auth": null }"""));
        failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().AcquireAsync(Request(r => r with { Env = loggedOut }), default));
        Assert.Contains("eas-cli is not logged in", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_eas.Calls);
    }

    [Fact]
    public async Task Authenticates_with_the_eas_cli_login_when_EXPO_TOKEN_is_not_set()
    {
        var loggedIn = Home(HomeWith(Login));
        var provider = Provider();
        var lease = await provider.AcquireAsync(Request(r => r with { Env = loggedIn }), default);
        await provider.ReleaseAsync(lease, ReleaseContext("run-login", loggedIn), default);
        Assert.Equal(
            [
                (EasOperation.Create, (string?)null, (string?)"session-secret"),
                (EasOperation.State, null, "session-secret"),
                (EasOperation.Stop, null, "session-secret"),
            ],
            _eas.Calls.Select(call => (call.Operation, call.Authorization, call.Session)));
    }

    [Fact]
    public async Task Prefers_EXPO_TOKEN_to_the_eas_cli_login()
    {
        await Provider().AcquireAsync(Request(r => r with { Env = Home(HomeWith(Login), "expo-test") }), default);
        Assert.Equal(("Bearer expo-test", null), (_eas.Calls[0].Authorization, _eas.Calls[0].Session));
    }

    [Fact]
    public async Task Reads_only_eas_clis_production_login_the_one_api_expo_dev_accepts()
    {
        var staging = Home(HomeWith(Login, ".expo-staging"));
        staging["EXPO_STAGING"] = "1";
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().AcquireAsync(Request(r => r with { Env = staging }), default));
        Assert.Contains("eas-cli is not logged in", failure.Message, StringComparison.Ordinal);
        var production = Home(HomeWith(Login));
        production["EXPO_STAGING"] = "1";
        await Provider().AcquireAsync(Request(r => r with { Env = production }), default);
        Assert.Equal((EasOperation.Create, "session-secret"), (_eas.Calls[0].Operation, _eas.Calls[0].Session));
    }

    [Fact]
    public async Task Stops_a_session_as_the_login_that_started_it_after_eas_logout()
    {
        var provider = Provider();
        var lease = await provider.AcquireAsync(Request(r => r with { Env = Home(HomeWith(Login)) }), default);
        await provider.ReleaseAsync(lease, ReleaseContext("run-logout", Home(_emptyHome)), default);
        Assert.Equal((EasOperation.Stop, "session-secret"), (_eas.Calls[^1].Operation, _eas.Calls[^1].Session));
    }

    [Fact]
    public async Task Keeps_the_login_that_started_a_session_for_a_release_that_is_tried_again()
    {
        var provider = Provider();
        var lease = await provider.AcquireAsync(Request(r => r with { Env = Home(HomeWith(Login)) }), default);
        _eas.Errors[EasOperation.Stop] = ("Internal server error", "INTERNAL_SERVER_ERROR");
        var failure = await Assert.ThrowsAsync<EasException>(() => provider.ReleaseAsync(lease, ReleaseContext("run-release-retry", Home(_emptyHome)), default));
        Assert.Contains("Internal server error", failure.Message, StringComparison.Ordinal);
        _eas.Errors.Remove(EasOperation.Stop);
        await provider.ReleaseAsync(lease, ReleaseContext("run-release-retry", Home(_emptyHome)), default);
        Assert.Equal(
            ["session-secret", "session-secret"],
            _eas.Calls.Where(call => call.Operation == EasOperation.Stop).Select(call => call.Session));
    }

    [Fact]
    public async Task Names_an_eas_cli_state_file_that_is_not_JSON()
    {
        var brokenHome = HomeWith("""{ "auth": "session-secret""");
        var env = Home(brokenHome);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().AcquireAsync(Request(r => r with { Env = env }), default));
        Assert.Equal($"the eas-cli login at {Path.Combine(brokenHome, ".expo", "state.json")} is not valid JSON; run `eas login` again, or set EXPO_TOKEN", failure.Message);

        // The parse error quotes the text it failed on, the secret included, so it is not kept as the cause.
        failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().AcquireAsync(Request(r => r with { Env = env }), default));
        Assert.Null(failure.InnerException);
        Assert.Empty(_eas.Calls);
    }

    [Fact]
    public async Task Reads_the_project_id_from_app_json_when_EasSimulators_names_none_once_per_run()
    {
        var projectRoot = ProjectWith(new Dictionary<string, string> { ["app.json"] = Linked("from-app-json") });
        var provider = Provider(new EasSimulatorsOptions());
        await provider.AcquireAsync(Request(r => r with { ProjectRoot = projectRoot, RunId = "run-once" }), default);
        File.WriteAllText(Path.Combine(projectRoot, "app.json"), Linked("relinked"));
        await provider.AcquireAsync(Request(r => r with { ProjectRoot = projectRoot, RunId = "run-once", Slot = 1 }), default);
        await provider.AcquireAsync(Request(r => r with { ProjectRoot = projectRoot, RunId = "run-next" }), default);
        Assert.Equal(["from-app-json", "from-app-json", "relinked"], AppIds());
    }

    [Fact]
    public async Task Reads_the_app_config_again_after_a_read_that_failed_in_the_same_run()
    {
        var projectRoot = ProjectWith(new Dictionary<string, string> { ["app.json"] = """{ "expo": { "name": "shop" } }""" });
        var provider = Provider(new EasSimulatorsOptions());
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.AcquireAsync(Request(r => r with { ProjectRoot = projectRoot, RunId = "run-config-retry" }), default));
        Assert.Contains("has no `extra.eas.projectId`", failure.Message, StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(projectRoot, "app.json"), Linked("after-eas-init"));
        await provider.AcquireAsync(Request(r => r with { ProjectRoot = projectRoot, RunId = "run-config-retry", Slot = 1 }), default);
        Assert.Equal("after-eas-init", _eas.Create(0)["appId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Stops_reading_the_app_config_when_the_run_is_interrupted()
    {
        var marker = Path.Combine(Temp("e2e-eas-marker-"), "started");
        var projectRoot = ProjectWith(Merge(
            new() { ["app.config.js"] = "" },
            FakeExpo($"require('node:fs').writeFileSync({JsonValue.Create(marker)!.ToJsonString()}, ''); setTimeout(() => {{}}, 60000);")));
        using var cancel = new CancellationTokenSource();
        var acquiring = Provider(new EasSimulatorsOptions()).AcquireAsync(Request(r => r with { ProjectRoot = projectRoot }), cancel.Token);
        await WaitForAsync(marker);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => acquiring);
        Assert.Empty(_eas.Calls);
    }

    [Fact]
    public async Task Reads_app_config_json_before_app_json_and_a_config_without_an_expo_key_as_the_whole_config()
    {
        var projectRoot = ProjectWith(new Dictionary<string, string>
        {
            ["app.config.json"] = """{ "extra": { "eas": { "projectId": " flat " } } }""",
            ["app.json"] = Linked("from-app-json"),
        });
        await Provider(new EasSimulatorsOptions()).AcquireAsync(Request(r => r with { ProjectRoot = projectRoot }), default);
        Assert.Equal("flat", _eas.Create(0)["appId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Prefers_the_projectId_option_to_the_app_config()
    {
        var projectRoot = ProjectWith(new Dictionary<string, string> { ["app.json"] = Linked("from-app-json") });
        await Provider().AcquireAsync(Request(r => r with { ProjectRoot = projectRoot }), default);
        Assert.Equal("p1", _eas.Create(0)["appId"]!.GetValue<string>());
    }

    [Fact]
    public async Task Evaluates_a_dynamic_app_config_with_the_projects_own_expo_config_in_the_runs_environment_without_dotenv_files_once_per_run_across_targets()
    {
        var projectRoot = ProjectWith(Merge(new() { ["app.config.ts"] = "export default {};", ["app.json"] = Linked("stale") }, FakeExpo()));
        var runEnv = Env;
        runEnv["SHOP_PROJECT_ID"] = "from-expo-config";

        // Two targets of one run, each with its own provider, and two slots of the first.
        var ios = Provider(new EasSimulatorsOptions());
        var android = Provider(new EasSimulatorsOptions());
        await Task.WhenAll(
            ios.AcquireAsync(Request(r => r with { ProjectRoot = projectRoot, Env = runEnv, RunId = "run-dynamic", Slot = 0 }), default),
            ios.AcquireAsync(Request(r => r with { ProjectRoot = projectRoot, Env = runEnv, RunId = "run-dynamic", Slot = 1 }), default),
            android.AcquireAsync(Request(r => r with { ProjectRoot = projectRoot, Env = runEnv, RunId = "run-dynamic", Platform = MobilePlatform.Android, TargetName = "android" }), default));
        Assert.Equal(["from-expo-config", "from-expo-config", "from-expo-config"], AppIds());
        Assert.Equal("config --json --type public EXPO_NO_DOTENV=1 CI=undefined\n", (await File.ReadAllTextAsync(Path.Combine(projectRoot, "expo-calls.txt"))).ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task Names_what_is_missing_when_the_app_config_links_no_project_and_starts_no_session()
    {
        const string pass = "pass `projectId` to EasSimulators, or run `eas init` to link the app to an EAS project";

        async Task<string> Failure(Dictionary<string, string>? files)
        {
            var root = ProjectWith(files);
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider(new EasSimulatorsOptions()).AcquireAsync(Request(r => r with { ProjectRoot = root }), default));
            return failure.Message.Replace(root, "<root>", StringComparison.Ordinal);
        }

        Assert.Equal($"no Expo app config (app.json or app.config.*) in <root>; {pass}", await Failure(null));
        Assert.Equal($"app.json has no `extra.eas.projectId`; {pass}", await Failure(new Dictionary<string, string> { ["app.json"] = """{ "expo": { "name": "shop" } }""" }));
        Assert.Contains("could not read the Expo app config at", await Failure(new Dictionary<string, string> { ["app.json"] = """{ "expo": """ }), StringComparison.Ordinal);
        Assert.Contains("is dynamic and `expo` is not installed there to evaluate it", await Failure(new Dictionary<string, string> { ["app.config.js"] = "module.exports = {};" }), StringComparison.Ordinal);
        Assert.Equal(
            $"app.config.js (through `expo config`) has no `extra.eas.projectId`; {pass}",
            await Failure(Merge(new() { ["app.config.js"] = "" }, FakeExpo("process.stdout.write('{}');"))));
        Assert.Contains(
            "`expo config` failed in",
            await Failure(Merge(new() { ["app.config.js"] = "" }, FakeExpo("process.stderr.write('Cannot find module ./secret'); process.exit(1);"))),
            StringComparison.Ordinal);
        Assert.Empty(_eas.Calls);
    }

    [Fact]
    public async Task Refuses_the_targets_app_appPath_for_an_app_EAS_already_installs_and_two_app_sources()
    {
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Provider(new EasSimulatorsOptions { ProjectId = "p1", BuildId = "b1" }).AcquireAsync(Request(r => r with { AppPath = "/builds/App.app" }), default));
        Assert.Contains("leave the target's `app.appPath` out", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_eas.Calls);
        Assert.Equal("s1", (await Provider().AcquireAsync(Request(r => r with { AppPath = "/builds/App.app" }), default)).Id);
        var invalid = Assert.Throws<ConfigurationException>(
            () => Provider(new EasSimulatorsOptions { ProjectId = "p1", BuildId = "b1", ApplicationArchiveUrl = "https://example.com/App.tar.gz" }));
        Assert.Equal("INVALID_CONFIG", invalid.Code);
        Assert.Contains("not both", invalid.Message, StringComparison.Ordinal);
    }

    private string[] AppIds() =>
        [.. _eas.Calls.Where(call => call.Operation == EasOperation.Create).Select(call => call.Variables["input"]!["appId"]!.GetValue<string>())];

    private static async Task WaitForAsync(string file)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!File.Exists(file))
        {
            await Task.Delay(20, timeout.Token);
        }
    }

    /// <summary>An API behind a proxy that answers every call with a 502 page.</summary>
    private sealed class Bad : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.BadGateway) { Content = new StringContent("Bad gateway") });
    }
}
