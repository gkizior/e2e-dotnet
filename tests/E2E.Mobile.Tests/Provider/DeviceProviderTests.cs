// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Mobile.Tests.Provider;

public sealed class DeviceProviderTests
{
    /// <summary>A scripted provider: leases <c>lease-n</c> against <c>https://n.example</c>, remembering every call.</summary>
    private sealed class ToyCloud(int? failSlot = null, string? installedApp = null, bool failRelease = false) : IDeviceProvider
    {
        private readonly TaskCompletionSource _allStarted = new();
        private int _started;

        public string Name => "toy-cloud";

        public List<DeviceRequest> Acquired { get; } = [];

        public List<DeviceLease> Released { get; } = [];

        public async Task<DeviceLease> AcquireAsync(DeviceRequest request, CancellationToken cancellationToken)
        {
            Acquired.Add(request);

            // Every slot starts before any lease comes back, as hosted sessions do.
            if (Interlocked.Increment(ref _started) == request.Slots)
            {
                _allStarted.SetResult();
            }

            if (request.Slot == failSlot)
            {
                throw new InvalidOperationException($"no capacity for slot {request.Slot}");
            }

            request.Log("starting");
            await _allStarted.Task;
            return (new DeviceLease
            {
                Id = $"lease-{request.Slot}",
                Daemon = new DeviceDaemon($"https://{request.Slot}.example", $"token-{request.Slot}"),
                Device = $"sim-{request.Slot}",
                InstalledApp = installedApp,
            });
        }

        public Task ReleaseAsync(DeviceLease lease, DeviceReleaseContext context, CancellationToken cancellationToken)
        {
            Released.Add(lease);
            return failRelease ? throw new InvalidOperationException("stop failed") : Task.CompletedTask;
        }
    }

    /// <summary>A provider whose acquire answers whatever a test scripts.</summary>
    private sealed class ScriptedProvider(string name, Func<DeviceRequest, Task<DeviceLease>> acquire) : IDeviceProvider
    {
        public string Name => name;

        public List<DeviceLease> Released { get; } = [];

        public Task<DeviceLease> AcquireAsync(DeviceRequest request, CancellationToken cancellationToken) => acquire(request);

        public Task ReleaseAsync(DeviceLease lease, DeviceReleaseContext context, CancellationToken cancellationToken)
        {
            Released.Add(lease);
            return Task.CompletedTask;
        }
    }

    /// <summary>A lease with extra fields of the provider's own: only the declared ones travel to a worker.</summary>
    private sealed class BookkeepingLease : DeviceLease
    {
        public int LeaseId { get; init; }

        public string Session { get; init; } = "";
    }

    private const string RejectedLease = "returned a lease without an id and a daemon baseUrl or JSON client configuration";

    private static Harness Over(IDeviceProvider provider, Func<MobileApp, MobileApp>? app = null, bool pinned = true) =>
        Harness.Create(options => options with { Provider = provider }, pinned, app);

    [Fact]
    public void Rejects_a_provider_without_a_name()
    {
        var error = Assert.Throws<ConfigurationException>(() => Over(new ScriptedProvider("", _ => Task.FromResult(new DeviceLease()))));
        Assert.Contains("non-empty `name`", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Leases_one_device_per_slot_at_prepare_warms_each_through_its_own_daemon_and_hands_the_bindings_to_the_workers()
    {
        var cloud = new ToyCloud();
        var h = Over(cloud);

        // A provider serves as many workers as the run has slots. The cap is only known once leased.
        Assert.Null(h.Engine.Pool.Size);
        var lines = new List<string>();
        var env = new Dictionary<string, string?> { ["DEVICE_SERVICE_TOKEN"] = "t" };
        var result = await h.Engine.PrepareAsync(h.PrepareInfo(2, log: lines.Add, env: env), CancellationToken.None);
        Assert.Equal(2, result.Workers);
        Assert.Equal(
            [(0, 2, MobilePlatform.Ios, "Settings", (string?)null), (1, 2, MobilePlatform.Ios, "Settings", null)],
            cloud.Acquired.Select(request => (request.Slot, request.Slots, request.Platform, request.App, request.AppPath)));
        Assert.Equal("t", cloud.Acquired[0].Env["DEVICE_SERVICE_TOKEN"]);
        Assert.Equal(Harness.ProjectRoot, cloud.Acquired[0].ProjectRoot);

        // The agent-device the package pins, so a provider can start a daemon of the same version.
        Assert.Equal(AgentDeviceVersion.Pinned, cloud.Acquired[0].AgentDeviceVersion);
        Assert.Equal(["leasing 2 ios device(s) from toy-cloud", "toy-cloud (1 of 2): starting", "toy-cloud (2 of 2): starting"], lines.Take(3));

        // The leases come back in whatever order the service answers.
        Assert.Equal(["toy-cloud: leased lease-0 (sim-0)", "toy-cloud: leased lease-1 (sim-1)"], lines.Skip(3).Take(2).Order());
        Assert.Equal(["booting sim-0 (1 of 2)", "booting sim-1 (2 of 2)"], lines.Skip(5));

        // Warmed like a local device, through the leased daemon: a fresh hosted simulator is the coldest device there is.
        Assert.Equal([new DeviceDaemon("https://0.example", "token-0"), new DeviceDaemon("https://1.example", "token-1")], h.Connections.Select(connection => connection?.Daemon));
        Assert.Equal(["devices.boot", "command.prepare", "apps.open", "devices.boot", "command.prepare", "apps.open"], h.Fake.Methods());
        JsonAssert.Args("""{"app":"Settings","platform":"ios","device":"sim-0"}""", h.Fake.Calls[2].Args);
        var variable = Harness.PoolVariableIn(result.Env, "IOS");
        JsonAssert.Args(
            """
            [
              {"leaseId":"lease-0","device":"sim-0","daemon":{"baseUrl":"https://0.example","authToken":"token-0"}},
              {"leaseId":"lease-1","device":"sim-1","daemon":{"baseUrl":"https://1.example","authToken":"token-1"}}
            ]
            """,
            JsonNode.Parse(result.Env[variable]));

        // A child worker reads its binding from the environment and drives that daemon and device.
        var worker = Over(cloud);
        await worker.Boot("ios", 1, Harness.WorkerEnv(result));
        Assert.Equal([new DeviceDaemon("https://1.example", "token-1")], worker.Connections.Select(connection => connection?.Daemon));
        Assert.Equal(["e2e-ios-1"], worker.Sessions);
        JsonAssert.Args("""{"platform":"ios","device":"sim-1"}""", worker.Fake.LastArgs("devices.boot"));

        // The runner-side engine reads its own bindings and releases the leases at finish.
        await h.Boot("ios", 0);
        Assert.Equal(new DeviceDaemon("https://0.example", "token-0"), h.Connections[^1]?.Daemon);
        var finished = new List<string>();
        await h.Engine.FinishAsync(Harness.FinishInfo(finished.Add), CancellationToken.None);
        Assert.Equal(["lease-0", "lease-1"], cloud.Released.Select(lease => lease.Id));
        Assert.Equal(["toy-cloud: released 2 device(s)"], finished);

        // Nothing left: a second finish releases nothing.
        await h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None);
        Assert.Equal(2, cloud.Released.Count);
    }

    [Fact]
    public async Task Asks_the_provider_for_the_resolved_build_and_leaves_the_install_to_the_suite_when_the_lease_did_not_without_the_install()
    {
        var cloud = new ToyCloud();
        var h = Over(cloud, app => app with { AppPath = "build/App.app" });
        var result = await h.Engine.PrepareAsync(h.PrepareInfo(1), CancellationToken.None);
        Assert.Equal(Path.Combine(Harness.ProjectRoot, "build/App.app"), cloud.Acquired[0].AppPath);

        // The lease installed nothing: the build is not on the device, so warm-up boots and starts the runner only, and the worker installs nothing.
        Assert.Equal(["devices.boot", "command.prepare"], h.Fake.Methods());
        var worker = Over(cloud, app => app with { AppPath = "build/App.app" });
        var session = await worker.Boot("ios", 0, Harness.WorkerEnv(result));
        Assert.Equal(["devices.boot"], worker.Fake.Methods());
        Assert.Equal(Path.Combine(Harness.ProjectRoot, "build/App.app"), session.BuildPath);
    }

    [Fact]
    public async Task Skips_the_build_install_when_the_lease_says_the_provider_installed_it_and_opens_that_app()
    {
        var cloud = new ToyCloud(installedApp: "com.example.app");
        var h = Over(cloud, app => app with { AppPath = "build/App.app" }, pinned: false);
        var result = await h.Engine.PrepareAsync(h.PrepareInfo(1), CancellationToken.None);

        // Installed by the provider: warm-up opens it right away.
        Assert.Equal(["devices.boot", "command.prepare", "apps.open"], h.Fake.Methods());
        JsonAssert.Args("""{"app":"com.example.app","platform":"ios","device":"sim-0"}""", h.Fake.LastArgs("apps.open"));
        var session = await h.Boot("ios", 0, Harness.WorkerEnv(result));
        Assert.DoesNotContain("apps.install", h.Fake.Methods());
        await session.RestartAsync(CancellationToken.None);
        var opened = h.Fake.LastArgs("apps.open")!;
        Assert.Equal("com.example.app", opened["app"]!.GetValue<string>());
        Assert.Equal("sim-0", opened["device"]!.GetValue<string>());
        Assert.True(opened["relaunch"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Rejects_a_lease_that_reports_an_installed_app_for_a_request_without_appPath()
    {
        var cloud = new ToyCloud(installedApp: "com.example.app");
        var h = Over(cloud);
        var failure = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(h.PrepareInfo(1), CancellationToken.None));
        Assert.Contains("reported an installed app for a request without `appPath`", failure.Message, StringComparison.Ordinal);

        // The lease was granted, so it is still released.
        await h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None);
        Assert.Equal(["lease-0"], cloud.Released.Select(lease => lease.Id));
    }

    [Fact]
    public async Task Rejects_a_lease_whose_installed_app_is_a_link_before_the_pool_opens_it_on_the_device()
    {
        var cloud = new ToyCloud(installedApp: "file:///etc/passwd");
        var h = Over(cloud, app => app with { AppPath = "build/App.app" }, pinned: false);
        var failure = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(h.PrepareInfo(1), CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", failure.Code);
        Assert.Contains("reported an installed app that is a link", failure.Message, StringComparison.Ordinal);
        Assert.Empty(h.Fake.Methods());
        await h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None);
        Assert.Equal(["lease-0"], cloud.Released.Select(lease => lease.Id));
    }

    [Fact]
    public async Task Leases_nothing_for_a_target_with_no_slots()
    {
        var cloud = new ToyCloud();
        var h = Over(cloud);
        var result = await h.Engine.PrepareAsync(h.PrepareInfo(0), CancellationToken.None);
        Assert.Null(result.Workers);
        Assert.Empty(result.Env);
        Assert.Empty(cloud.Acquired);
        await h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None);
        Assert.Empty(cloud.Released);
    }

    [Fact]
    public async Task Hands_the_workers_only_the_declared_lease_fields_and_gives_release_the_providers_own_object_back()
    {
        var granted = new List<DeviceLease>();
        var bookkeeping = new ScriptedProvider(
            "notes",
            request =>
            {
                // A provider may keep a `leaseId` of its own on the lease. The worker's comes from `Id`.
                var lease = new BookkeepingLease { Id = $"l-{request.Slot}", LeaseId = 42, Daemon = new DeviceDaemon("https://d.example"), Session = new string('x', 100) };
                granted.Add(lease);
                return Task.FromResult<DeviceLease>(lease);
            });
        var h = Over(bookkeeping);
        var result = await h.Engine.PrepareAsync(h.PrepareInfo(1), CancellationToken.None);
        JsonAssert.Args("""[{"leaseId":"l-0","daemon":{"baseUrl":"https://d.example"}}]""", JsonNode.Parse(result.Env[Harness.PoolVariableIn(result.Env, "IOS")]));
        await h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None);
        Assert.Equal(granted, bookkeeping.Released);
        Assert.Same(granted[0], bookkeeping.Released[0]);
    }

    [Fact]
    public async Task Carries_client_configuration_to_the_worker_next_to_the_daemon_and_accepts_a_lease_with_only_that()
    {
        var scope = new JsonObject
        {
            ["stateDir"] = "/tmp/devices",
            ["tenant"] = "cloud",
            ["runId"] = "run-1",
            ["leaseId"] = "lease-0",
            ["leaseBackend"] = "ios-instance",
            ["leaseProvider"] = "cloud",
        };
        var scoped = new ScriptedProvider(
            "scoped",
            request => Task.FromResult(
                request.Slot == 0
                    ? new DeviceLease { Id = "l-0", Client = (JsonObject)scope.DeepClone() }
                    : new DeviceLease { Id = "l-1", Daemon = new DeviceDaemon("https://1.example"), Client = new JsonObject { ["providerOsVersion"] = "18.0" } }));
        var h = Over(scoped);
        var result = await h.Engine.PrepareAsync(h.PrepareInfo(2), CancellationToken.None);

        // Warm-up already drives each device with its lease's configuration.
        Assert.Equal(["l-0", "l-1"], h.Connections.Cast<SlotBinding>().Select(connection => connection.LeaseId));
        Assert.True(JsonNode.DeepEquals(scope, h.Connections[0]!.Client));
        Assert.Equal(new DeviceDaemon("https://1.example"), h.Connections[1]!.Daemon);
        JsonAssert.Args("""{"providerOsVersion":"18.0"}""", h.Connections[1]!.Client);
        var variable = Harness.PoolVariableIn(result.Env, "IOS");
        var handed = JsonNode.Parse(result.Env[variable])!.AsArray();
        Assert.True(JsonNode.DeepEquals(scope, handed[0]!["client"]));
        var worker = Over(scoped);
        await worker.Boot("ios", 0, Harness.WorkerEnv(result));
        Assert.True(JsonNode.DeepEquals(scope, worker.Connections[0]!.Client));
    }

    [Fact]
    public async Task Refuses_a_lease_that_names_neither_a_daemon_nor_client_configuration_or_a_client_with_reserved_keys_and_still_releases_it()
    {
        var leases = new[]
        {
            new DeviceLease { Id = "bare" },
            new DeviceLease { Id = "session", Client = new JsonObject { ["session"] = "mine", ["leaseId"] = "x" } },
            new DeviceLease { Id = "daemon-keys", Client = new JsonObject { ["daemonBaseUrl"] = "https://d.example" } },
            new DeviceLease { Id = "transport", Client = new JsonObject { ["leaseId"] = "x", ["daemonTransport"] = "http" } },
            new DeviceLease { Id = "nan", Client = new JsonObject { ["leaseId"] = "x", ["leaseTtlMs"] = double.NaN } },
            new DeviceLease { Id = "infinity", Client = new JsonObject { ["leaseId"] = "x", ["leaseTtlMs"] = double.PositiveInfinity } },
            new DeviceLease { Id = "negative-infinity", Client = new JsonObject { ["leaseId"] = "x", ["leaseTtlMs"] = double.NegativeInfinity } },
        };
        foreach (var lease in leases)
        {
            var odd = new ScriptedProvider("odd", _ => Task.FromResult(lease));
            var h = Over(odd);
            var failure = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(h.PrepareInfo(1), CancellationToken.None));
            Assert.Contains(RejectedLease, failure.Message, StringComparison.Ordinal);

            // The provider allocated a device for it, so it is billed until released: finish hands back the very object.
            await h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None);
            Assert.Same(lease, Assert.Single(odd.Released));
        }
    }

    [Fact]
    public async Task Has_nothing_to_release_for_a_lease_without_an_id()
    {
        var nameless = new ScriptedProvider("nameless", _ => Task.FromResult(new DeviceLease { Daemon = new DeviceDaemon("https://d.example") }));
        var h = Over(nameless);
        var failure = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(h.PrepareInfo(1), CancellationToken.None));
        Assert.Contains(RejectedLease, failure.Message, StringComparison.Ordinal);
        await h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None);
        Assert.Empty(nameless.Released);
    }

    [Fact]
    public async Task Releases_a_good_lease_and_a_rejected_one_from_the_same_prepare_and_warms_neither()
    {
        var bad = new DeviceLease { Id = "bad" };
        var mixed = new ScriptedProvider(
            "mixed",
            request => Task.FromResult(request.Slot == 0 ? new DeviceLease { Id = "lease-0", Daemon = new DeviceDaemon("https://0.example"), Device = "sim-0" } : bad));
        var h = Over(mixed);
        var failure = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(h.PrepareInfo(2), CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", failure.Code);
        Assert.Contains(RejectedLease, failure.Message, StringComparison.Ordinal);

        // The bind failed before the warm-up, so no device was booted or opened, and nothing is left to close.
        Assert.Empty(h.Fake.Methods());
        await h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None);
        Assert.Equal(["lease-0", "bad"], mixed.Released.Select(lease => lease.Id));
        Assert.Same(bad, mixed.Released[1]);
        Assert.Empty(h.Fake.Methods());
    }

    [Fact]
    public async Task Rejects_bindings_too_large_for_a_worker_environment_by_bytes()
    {
        // Multi-byte characters: the cap is on bytes, not string length.
        var oversized = new ScriptedProvider(
            "huge",
            request => Task.FromResult(new DeviceLease { Id = $"l-{request.Slot}", Daemon = new DeviceDaemon("https://d.example", new string('ż', 9_000)) }));
        var h = Over(oversized);
        var failure = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(h.PrepareInfo(1), CancellationToken.None));
        Assert.Contains("the worker environment carries at most 16384", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Holds_the_slots_that_leased_when_another_fails_so_finish_releases_them_and_fails_the_run_before_any_test()
    {
        var cloud = new ToyCloud(failSlot: 1);
        var h = Over(cloud);
        var failure = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(h.PrepareInfo(2), CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", failure.Code);
        Assert.Contains("device provider \"toy-cloud\" could not lease a device: no capacity for slot 1", failure.Message, StringComparison.Ordinal);
        await h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None);
        Assert.Equal(["lease-0"], cloud.Released.Select(lease => lease.Id));
    }

    [Fact]
    public async Task Treats_an_acquire_that_throws_before_its_first_await_like_any_other_failed_slot()
    {
        var sync = new ScriptedProvider(
            "sync",
            request => request.Slot == 1
                ? throw new InvalidOperationException("token missing")
                : Task.FromResult(new DeviceLease { Id = $"l-{request.Slot}", Daemon = new DeviceDaemon("https://d.example") }));
        var h = Over(sync);
        var failure = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(h.PrepareInfo(2), CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", failure.Code);
        Assert.Contains("could not lease a device: token missing", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reports_a_release_failure_once_every_lease_was_tried_and_a_slot_outside_the_leases_as_an_engine_defect()
    {
        var cloud = new ToyCloud(failRelease: true);
        var h = Over(cloud);
        await h.Engine.PrepareAsync(h.PrepareInfo(2), CancellationToken.None);
        var failure = await Assert.ThrowsAsync<EngineException>(() => h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None));
        Assert.Contains("could not release a device: stop failed", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["lease-0", "lease-1"], cloud.Released.Select(lease => lease.Id));

        var again = Over(cloud);
        await again.Engine.PrepareAsync(again.PrepareInfo(1), CancellationToken.None);
        var outside = await Assert.ThrowsAsync<EngineException>(() => again.Boot("ios", 1));
        Assert.Contains("worker slot 1 is outside a device pool of 1", outside.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_prepared_lease_a_provider_backed_worker_falls_back_to_the_local_daemon_and_finish_has_nothing_to_release()
    {
        var cloud = new ToyCloud();
        var h = Over(cloud);
        await h.Boot("ios", 0);
        Assert.Equal([null], h.Connections);
        JsonAssert.Args("""{"platform":"ios"}""", h.Fake.LastArgs("devices.boot"));
        await h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None);
        Assert.Empty(cloud.Released);
    }
}
