// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Mobile.Tests.Errors;

public sealed class AutomationRunnerFailuresThroughTheEngineTests
{
    private static AgentDeviceError RunnerBusy() => new(
        "COMMAND_FAILED",
        "The iOS runner is still finishing a previous command that exceeded its execution watchdog (usually an accessibility capture on a heavy or animating screen).",
        new Dictionary<string, object?>
        {
            ["runnerErrorCode"] = "RUNNER_BUSY",
            ["retriable"] = true,
            ["hint"] = "Wait a few seconds and retry. If snapshots keep failing on this screen, use screenshot as visual truth and interact by coordinates, or navigate to another screen.",
        });

    private static AgentDeviceError InvalidViewport() => new(
        "COMMAND_FAILED",
        "regular iOS snapshot presentation requires a valid viewport",
        new Dictionary<string, object?>
        {
            ["reason"] = "invalid-viewport",
            ["field"] = "viewport",
            ["hint"] = "Use snapshot --raw to inspect the acquired iOS tree; regular presentation requires valid viewport evidence.",
        });

    private sealed class ToyCloud : IDeviceProvider
    {
        public string Name => "toy-cloud";

        public List<string> Released { get; } = [];

        public Task<DeviceLease> AcquireAsync(DeviceRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceLease { Id = $"lease-{request.Slot}", Daemon = new DeviceDaemon($"https://{request.Slot}.example"), Device = $"sim-{request.Slot}" });

        public Task ReleaseAsync(DeviceLease lease, DeviceReleaseContext context, CancellationToken cancellationToken)
        {
            Released.Add(lease.Id);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Fails_prepare_when_the_runner_is_busy_at_warm_up_with_the_session_and_device_in_the_message_any_other_open_failure_is_still_logged()
    {
        var h = Harness.Create(options => options with { Device = "iPhone 17 Pro" });
        h.Fake.Respond(DeviceMethods.AppsOpen, _ => throw RunnerBusy());
        var lines = new List<string>();
        var busy = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(h.PrepareInfo(log: lines.Add), CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", busy.Code);
        Assert.Contains(
            "open Settings failed: the iOS automation runner is still finishing a command that overran its watchdog (session e2e-ios-0 on iPhone 17 Pro)",
            busy.Message,
            StringComparison.Ordinal);
        var again = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(h.PrepareInfo(log: lines.Add), CancellationToken.None));
        Assert.Contains("npx agent-device daemon stop", again.Message, StringComparison.Ordinal);
        Assert.Equal(["booting iPhone 17 Pro (1 of 1)", "booting iPhone 17 Pro (1 of 1)"], lines);

        h.Fake.Respond(DeviceMethods.AppsOpen, _ => throw new InvalidOperationException("runner still installing"));
        await h.Engine.PrepareAsync(h.PrepareInfo(log: lines.Add), CancellationToken.None);
        Assert.Matches(@"Settings did not open.*runner still installing", lines[3]);
    }

    [Fact]
    public async Task Closes_every_session_a_failed_warm_up_opened_at_finish_and_still_releases_the_leases_when_a_close_fails()
    {
        var cloud = new ToyCloud();
        var h = Harness.Create(options => options with { Provider = cloud });
        h.Fake.Respond(DeviceMethods.AppsOpen, args => args?["device"]?.GetValue<string>() == "sim-1" ? throw RunnerBusy() : new JsonObject());
        int Closes() => h.Fake.Methods().Count(method => method == "sessions.close");
        var info = h.PrepareInfo(2);
        var failure = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(info, CancellationToken.None));
        Assert.Contains("(session e2e-ios-1 on sim-1)", failure.Message, StringComparison.Ordinal);
        Assert.Equal(["e2e-ios-0", "e2e-ios-1"], h.Sessions);
        Assert.Equal(0, Closes());

        h.Fake.Respond(DeviceMethods.SessionsClose, _ => throw new InvalidOperationException("daemon gone"));
        await h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None);
        Assert.Equal(2, Closes());
        Assert.Equal(["lease-0", "lease-1"], cloud.Released);

        // Nothing left: a second finish closes and releases nothing.
        await h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None);
        Assert.Equal(2, Closes());
        Assert.Equal(2, cloud.Released.Count);
    }

    [Fact]
    public async Task Closes_the_sessions_a_local_pool_warmed_too_a_boot_that_failed_included()
    {
        var h = Harness.Create(options => options with { Devices = ["iPhone 17", "iPhone 17 Pro"] });
        h.Fake.Respond(DeviceMethods.DevicesBoot, args => args?["device"]?.GetValue<string>() == "iPhone 17 Pro" ? throw new AgentDeviceError("DEVICE_NOT_FOUND", "no such device") : new JsonObject());
        var failure = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(h.PrepareInfo(2), CancellationToken.None));
        Assert.Equal("boot failed: no such device", failure.Message);
        await h.Engine.FinishAsync(Harness.FinishInfo(), CancellationToken.None);
        Assert.Equal(2, h.Fake.Methods().Count(method => method == "sessions.close"));
    }

    [Fact]
    public async Task Fails_prepare_when_the_runner_is_wedged_at_warm_up()
    {
        var h = Harness.Create(options => options with { Device = "iPhone 17 Pro" });
        h.Fake.Respond(DeviceMethods.AppsOpen, _ => throw new AgentDeviceError("RUNNER_WEDGED", "The iOS runner main thread has been stuck in abandoned work for 120 seconds and cannot recover on its own."));
        var wedged = await Assert.ThrowsAsync<EngineException>(() => h.Engine.PrepareAsync(h.PrepareInfo(), CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", wedged.Code);
        Assert.Contains("the iOS automation runner is wedged", wedged.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Takes_a_snapshot_the_runner_could_not_present_once_more_and_fails_the_observation_on_the_second_failure()
    {
        var h = Harness.Create(options => options with { Device = "iPhone 17 Pro" });
        var failures = 1;
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => failures-- > 0 ? throw InvalidViewport() : FakeDeviceClient.SettingsSnapshot());
        var session = await h.Boot();
        var snapshot = await session.ObserveAsync(CancellationToken.None);
        Assert.NotEmpty(snapshot.Roots[0].Children);
        Assert.Equal(2, h.Fake.Methods().Count(method => method == "capture.snapshot"));

        failures = 2;
        var second = await Assert.ThrowsAsync<EngineException>(() => session.ObserveAsync(CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", second.Code);
        Assert.Contains(
            "snapshot failed: the iOS automation runner could not present the accessibility snapshot (session e2e-ios-simulator-0 on iPhone 17 Pro)",
            second.Message,
            StringComparison.Ordinal);
        Assert.Equal(4, h.Fake.Methods().Count(method => method == "capture.snapshot"));
    }

    [Fact]
    public async Task Names_the_session_and_device_when_a_node_action_or_the_screen_scroll_meets_a_busy_runner()
    {
        var h = Harness.Create(options => options with { Device = "iPhone 17 Pro" });
        var session = await h.Boot();
        var screen = (await session.ObserveAsync(CancellationToken.None)).Roots[0];
        var about = Screens.Named(screen, "About");
        h.Fake.Respond(DeviceMethods.InteractionsPress, _ => throw RunnerBusy());
        var tap = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(about, new LocatorAction.Tap(), CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", tap.Code);
        Assert.False(tap.Retryable);
        Assert.Contains(
            "perform tap failed: the iOS automation runner is still finishing a command that overran its watchdog (session e2e-ios-simulator-0 on iPhone 17 Pro)",
            tap.Message,
            StringComparison.Ordinal);
        h.Fake.Respond(DeviceMethods.InteractionsScroll, _ => throw RunnerBusy());
        var scroll = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(screen, new LocatorAction.Swipe(ScrollDirection.Down), CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", scroll.Code);
        Assert.Contains(
            "swipe failed: the iOS automation runner is still finishing a command that overran its watchdog (session e2e-ios-simulator-0 on iPhone 17 Pro)",
            scroll.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_retake_a_snapshot_that_failed_for_any_other_reason()
    {
        var h = Harness.Create();
        h.Fake.Respond(DeviceMethods.CaptureSnapshot, _ => throw RunnerBusy());
        var session = await h.Boot();
        var busy = await Assert.ThrowsAsync<EngineException>(() => session.ObserveAsync(CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", busy.Code);
        Assert.Contains("the iOS automation runner is still finishing a command that overran its watchdog (session e2e-ios-simulator-0)", busy.Message, StringComparison.Ordinal);
        Assert.Single(h.Fake.Methods(), method => method == "capture.snapshot");
    }
}
