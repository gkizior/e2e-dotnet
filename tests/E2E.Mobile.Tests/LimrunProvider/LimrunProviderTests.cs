// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Net;
using System.Text.Json.Nodes;
using E2E.Mobile.Limrun;

namespace E2E.Mobile.Tests.LimrunProvider;

/// <summary>
/// <see cref="LimrunSimulators"/> against a fake Limrun API and a fake host for the daemon and the lease: what it
/// creates, the environment each daemon starts with, the build it hands to the driver, and what it cleans up when
/// setup fails, a stop fails, or the device has expired. Ports <c>packages/limrun/tests/unit/provider.test.ts</c>.
/// </summary>
public sealed class LimrunProviderTests
{
    private static readonly IReadOnlyDictionary<string, string?> RunEnvironment = new Dictionary<string, string?> { ["LIMRUN_API_KEY"] = "run-key", ["PATH"] = "/bin" };

    private readonly FakeLimrun _api = new();
    private readonly FakeLimrunHost _host = new();

    private LimrunSimulators Provider(LimrunSimulatorsOptions? options = null) =>
        new(options, _host, _api.Client, static (_, _) => Task.CompletedTask);

    private static DeviceRequest Request(
        MobilePlatform platform = MobilePlatform.Ios,
        string targetName = "ios",
        int slot = 0,
        IReadOnlyDictionary<string, string?>? env = null,
        string? app = null,
        string? appPath = null,
        string agentDeviceVersion = AgentDeviceVersion.Pinned) => new()
        {
            Platform = platform,
            RunId = "run-a",
            TargetName = targetName,
            Slot = slot,
            Slots = 1,
            AgentDeviceVersion = agentDeviceVersion,
            App = app,
            AppPath = appPath,
            ProjectRoot = "/project",
            Env = env ?? RunEnvironment,
            Log = _ => { },
        };

    private static DeviceReleaseContext Cleanup(IReadOnlyDictionary<string, string?>? env = null) =>
        new() { RunId = "run-a", TargetName = "ios", Env = env ?? RunEnvironment, Log = _ => { } };

    [Fact]
    public async Task Requires_the_run_key_and_matching_driver_before_creating_a_device()
    {
        var provider = Provider();

        var missingKey = await Assert.ThrowsAsync<ConfigurationException>(() => provider.AcquireAsync(Request(env: new Dictionary<string, string?>()), CancellationToken.None));
        Assert.Contains("LIMRUN_API_KEY", missingKey.Message, StringComparison.Ordinal);

        var mismatch = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.AcquireAsync(Request(agentDeviceVersion: "0.0.1"), CancellationToken.None));
        Assert.Contains("0.0.1", mismatch.Message, StringComparison.Ordinal);
        Assert.Contains(AgentDeviceVersion.Pinned, mismatch.Message, StringComparison.Ordinal);

        Assert.Empty(_api.Calls);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://run.example")]
    public async Task Selects_the_API_endpoint_from_the_run_environment(string? baseUrl)
    {
        var provider = Provider();
        var env = new Dictionary<string, string?> { ["LIMRUN_API_KEY"] = "run-key", ["LIMRUN_BASE_URL"] = baseUrl };

        var lease = await provider.AcquireAsync(Request(env: env), CancellationToken.None);

        var create = Assert.Single(_api.Of("POST", "ios_instances"));
        Assert.Equal("Bearer run-key", create.Authorization);
        Assert.Equal($"{baseUrl ?? "https://api.limrun.com"}/v1/ios_instances", create.Uri.GetLeftPart(UriPartial.Path));
        await provider.ReleaseAsync(lease, Cleanup(), CancellationToken.None);
    }

    [Fact]
    public async Task Isolates_concurrent_slots_and_targets_uses_run_credentials_and_never_mutates_process_env()
    {
        var before = Environment.GetEnvironmentVariables();
        var provider = Provider(new LimrunSimulatorsOptions
        {
            Region = "eu",
            HardTimeout = "30m",
            Ios = new LimrunIosOptions { Model = LimrunIosModel.Ipad },
            Android = new LimrunAndroidOptions { OsVersion = "16" },
        });
        var env = new Dictionary<string, string?>
        {
            ["LIMRUN_API_KEY"] = "run-key",
            ["LIM_IOS_INSTANCE_URL"] = "foreign",
            ["LIM_IOS_INSTANCE_TOKEN"] = "foreign-token",
            ["AGENT_DEVICE_STATE_DIR"] = "foreign-dir",
            ["AGENT_DEVICE_DAEMON_POLICY"] = "/policy.json",
        };

        var leases = await Task.WhenAll(
            provider.AcquireAsync(Request(env: env), CancellationToken.None),
            provider.AcquireAsync(Request(MobilePlatform.Android, "android", 1, env), CancellationToken.None));
        try
        {
            Assert.All(_api.Calls, call => Assert.Equal("Bearer run-key", call.Authorization));
            JsonAssert.Args(
                """{"hardTimeout":"30m","region":"eu","model":"ipad"}""",
                Assert.Single(_api.Of("POST", "ios_instances")).Body!["spec"]);
            JsonAssert.Args(
                """{"hardTimeout":"30m","region":"eu","clues":[{"kind":"OSVersion","osVersion":"16"}]}""",
                Assert.Single(_api.Of("POST", "android_instances")).Body!["spec"]);
            var ios = Assert.Single(_host.Started, started => started.Request.Platform == MobilePlatform.Ios);
            var android = Assert.Single(_host.Started, started => started.Request.Platform == MobilePlatform.Android);
            Assert.NotEqual(ios.Request.StateDirectory, android.Request.StateDirectory);
            Assert.Equal("run-key", ios.Environment["LIMRUN_API_KEY"]);
            Assert.Equal("https://device.example", ios.Environment["LIM_IOS_INSTANCE_URL"]);
            Assert.Equal("instance-secret", ios.Environment["LIM_IOS_INSTANCE_TOKEN"]);
            Assert.False(android.Environment.ContainsKey("LIM_IOS_INSTANCE_URL"));
            Assert.Equal("https://device.example", android.Environment["LIM_ANDROID_INSTANCE_URL"]);
            Assert.Equal("wss://adb.example", android.Environment["LIM_ANDROID_INSTANCE_ADB_URL"]);
            Assert.False(ios.Environment.ContainsKey("AGENT_DEVICE_STATE_DIR"));
            Assert.Equal("/policy.json", ios.Environment["AGENT_DEVICE_DAEMON_POLICY"]);
            Assert.Equal(before.Count, Environment.GetEnvironmentVariables().Count);
            foreach (DictionaryEntry entry in before)
            {
                Assert.Equal(entry.Value, Environment.GetEnvironmentVariable((string)entry.Key));
            }
        }
        finally
        {
            foreach (var lease in leases)
            {
                await provider.ReleaseAsync(lease, Cleanup(), CancellationToken.None);
            }
        }

        Assert.Contains(_api.Of("DELETE", "ios_instances"), call => call.Uri.AbsolutePath.EndsWith("/ios-test", StringComparison.Ordinal));
        Assert.Equal(2, _host.HeartbeatsStopped);
        Assert.Contains(_api.Of("DELETE", "android_instances"), call => call.Uri.AbsolutePath.EndsWith("/android-test", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Hands_installation_to_the_driver_and_reports_only_the_installed_identity()
    {
        _host.OnStart = _ => new LimrunBinding(new JsonObject { ["stateDir"] = "/state" }, "device", "dev.example");
        var provider = Provider();

        var lease = await provider.AcquireAsync(Request(app: "dev.example", appPath: "/project/build/App.app"), CancellationToken.None);

        Assert.Equal("dev.example", lease.InstalledApp);
        var started = Assert.Single(_host.Started).Request;
        Assert.Equal("/project/build/App.app", started.AppPath);
        Assert.Equal("dev.example", started.App);
        await provider.ReleaseAsync(lease, Cleanup(), CancellationToken.None);
    }

    [Fact]
    public async Task Cleans_a_device_when_driver_setup_or_install_fails_using_a_fresh_cleanup_signal()
    {
        using var run = new CancellationTokenSource();
        _host.OnStart = _ =>
        {
            run.Cancel();
            throw new InvalidOperationException("install failed");
        };

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().AcquireAsync(Request(), run.Token));

        Assert.Contains("install failed", failure.Message, StringComparison.Ordinal);
        Assert.NotEmpty(_host.Stopped);
        var delete = Assert.Single(_api.Of("DELETE", "ios_instances"));
        Assert.EndsWith("/ios-test", delete.Uri.AbsolutePath, StringComparison.Ordinal);
        Assert.False(delete.Cancelled);
    }

    [Fact]
    public async Task Tolerates_a_read_model_404_while_waiting_for_readiness()
    {
        _api.ScriptInstance("POST", "ios_instances", "creating");
        _api.Script("GET", "ios_instances", () => FakeLimrun.Status(HttpStatusCode.NotFound));
        var provider = Provider();

        var lease = await provider.AcquireAsync(Request(), CancellationToken.None);

        Assert.Equal(2, _api.Of("GET", "ios_instances").Count());
        await provider.ReleaseAsync(lease, Cleanup(), CancellationToken.None);
    }

    [Fact]
    public async Task Still_deletes_the_device_if_stopping_the_daemon_fails_and_reports_the_cleanup_failure()
    {
        var provider = Provider();
        var lease = await provider.AcquireAsync(Request(), CancellationToken.None);
        _host.StopFailures.Enqueue(new InvalidOperationException("daemon did not stop"));

        var failure = await Assert.ThrowsAsync<AggregateException>(() => provider.ReleaseAsync(lease, Cleanup(), CancellationToken.None));

        Assert.Contains("Could not fully release", failure.Message, StringComparison.Ordinal);
        Assert.NotEmpty(_api.Of("DELETE", "ios_instances"));
        await provider.ReleaseAsync(lease, Cleanup(), CancellationToken.None);
    }

    [Fact]
    public async Task Treats_a_device_that_expired_before_release_as_already_cleaned_up()
    {
        var provider = Provider();
        var lease = await provider.AcquireAsync(Request(), CancellationToken.None);
        _api.Script("DELETE", "ios_instances", () => FakeLimrun.Status(HttpStatusCode.NotFound));

        await provider.ReleaseAsync(lease, Cleanup(), CancellationToken.None);
        await provider.ReleaseAsync(lease, Cleanup(), CancellationToken.None);

        Assert.Single(_api.Of("DELETE", "ios_instances"));
    }

    [Fact]
    public async Task Refuses_missing_Android_tools_before_allocating_anything()
    {
        _host.AndroidToolsFailure = new InvalidOperationException("adb missing");

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Provider().AcquireAsync(Request(MobilePlatform.Android), CancellationToken.None));

        Assert.Contains("adb missing", failure.Message, StringComparison.Ordinal);
        Assert.Empty(_api.Of("POST", "android_instances"));
    }
}
