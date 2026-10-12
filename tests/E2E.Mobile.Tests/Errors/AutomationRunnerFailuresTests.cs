// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Mobile.Tests.Errors;

public sealed class AutomationRunnerFailuresTests
{
    private const string Recovery =
        "If the next run meets it again, stop the daemon (`npx agent-device daemon stop`) and reboot the simulator (`xcrun simctl shutdown <udid>`, then `boot`).";

    private static EngineException Engine(Exception error) => Assert.IsType<EngineException>(error);

    private static Dictionary<string, object?> Details(params (string Key, object? Value)[] details) =>
        details.ToDictionary(detail => detail.Key, detail => detail.Value);

    /// <summary><c>RUNNER_BUSY</c> as the client receives it: folded under <c>COMMAND_FAILED</c>, the wire code in the details.</summary>
    private static AgentDeviceError RunnerBusy() => new(
        "COMMAND_FAILED",
        "The iOS runner is still finishing a previous command that exceeded its execution watchdog (usually an accessibility capture on a heavy or animating screen).",
        Details(
            ("runnerErrorCode", "RUNNER_BUSY"),
            ("retriable", true),
            ("hint", "Wait a few seconds and retry. If snapshots keep failing on this screen, use screenshot as visual truth and interact by coordinates, or navigate to another screen.")));

    /// <summary>The presentation failure as the client receives it: <c>COMMAND_FAILED</c> with the failed check in <c>reason</c>.</summary>
    private static AgentDeviceError InvalidViewport() => new(
        "COMMAND_FAILED",
        "regular iOS snapshot presentation requires a valid viewport",
        Details(
            ("reason", "invalid-viewport"),
            ("field", "viewport"),
            ("hint", "Use snapshot --raw to inspect the acquired iOS tree; regular presentation requires valid viewport evidence.")));

    [Fact]
    public void Names_a_busy_runner_the_session_and_device_and_the_recovery_under_ENGINE_FAILURE_without_the_model_facing_hint()
    {
        var translated = Engine(DeviceErrors.TranslateError(RunnerBusy(), "snapshot", "session e2e-ios-0 on iPhone 17 Pro"));
        Assert.Equal("ENGINE_FAILURE", translated.Code);
        Assert.False(translated.Retryable);
        Assert.Contains(
            "snapshot failed: the iOS automation runner is still finishing a command that overran its watchdog (session e2e-ios-0 on iPhone 17 Pro): ",
            translated.Message,
            StringComparison.Ordinal);
        Assert.Contains(Recovery, translated.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Hint:", translated.Message, StringComparison.Ordinal);
        Assert.True(DeviceErrors.IsRunnerFailure(translated));
        Assert.False(DeviceErrors.IsSnapshotPresentationFailure(translated));
    }

    [Fact]
    public void Names_a_wedged_runner_by_its_top_level_code_and_a_watchdog_overrun_by_its_wire_code()
    {
        var wedged = Engine(DeviceErrors.TranslateError(
            new AgentDeviceError("RUNNER_WEDGED", "The iOS runner main thread has been stuck in abandoned work for 120 seconds and cannot recover on its own."),
            "perform tap"));
        Assert.Equal("ENGINE_FAILURE", wedged.Code);
        Assert.Contains("perform tap failed: the iOS automation runner is wedged: its main thread is stuck in abandoned work: ", wedged.Message, StringComparison.Ordinal);
        Assert.Contains(Recovery, wedged.Message, StringComparison.Ordinal);
        Assert.True(DeviceErrors.IsRunnerFailure(wedged));
        var overran = Engine(DeviceErrors.TranslateError(
            new AgentDeviceError("COMMAND_FAILED", "snapshot timed out on the runner main thread", Details(("runnerErrorCode", "MAIN_THREAD_TIMEOUT"))),
            "snapshot",
            "session e2e-ios-0"));
        Assert.Equal("ENGINE_FAILURE", overran.Code);
        Assert.Contains(
            "the iOS automation runner's main thread overran its watchdog on this command (session e2e-ios-0)",
            overran.Message,
            StringComparison.Ordinal);
        Assert.Contains($"The app is fine. Rerun once it has drained. {Recovery}", overran.Message, StringComparison.Ordinal);
        Assert.True(DeviceErrors.IsRunnerFailure(overran));
        Assert.False(DeviceErrors.IsRunnerFailure(DeviceErrors.TranslateError(new AgentDeviceError("COMMAND_FAILED", "xcrun exploded"), "boot")));
    }

    [Fact]
    public void Names_a_snapshot_the_runner_could_not_present_by_the_failed_check_or_the_upstream_code()
    {
        var byReason = Engine(DeviceErrors.TranslateError(InvalidViewport(), "snapshot", "session e2e-ios-0 on iPhone 17 Pro"));
        Assert.Equal("ENGINE_FAILURE", byReason.Code);
        Assert.False(byReason.Retryable);
        Assert.Contains(
            "snapshot failed: the iOS automation runner could not present the accessibility snapshot (session e2e-ios-0 on iPhone 17 Pro): ",
            byReason.Message,
            StringComparison.Ordinal);
        Assert.Contains(Recovery, byReason.Message, StringComparison.Ordinal);
        Assert.True(DeviceErrors.IsSnapshotPresentationFailure(byReason));
        Assert.False(DeviceErrors.IsRunnerFailure(byReason));
        var byCode = DeviceErrors.TranslateError(
            new AgentDeviceError("IOS_SNAPSHOT_ENGINE_FAILED", "iOS snapshot graph contains an invalid node depth"),
            "snapshot");
        Assert.Contains(
            "could not present the accessibility snapshot: iOS snapshot graph contains an invalid node depth",
            byCode.Message,
            StringComparison.Ordinal);
        Assert.True(DeviceErrors.IsSnapshotPresentationFailure(byCode));
        Assert.False(DeviceErrors.IsSnapshotPresentationFailure(DeviceErrors.TranslateError(new AgentDeviceError("COMMAND_FAILED", "plain"), "snapshot")));
    }

    [Fact]
    public void Recognizes_every_presentation_check_agent_device_raises_or_rewraps_by_reason_alone()
    {
        // The text of each is agent-device's; the reason is what the engine reads.
        (string Reason, string Text)[] raised =
        [
            ("invalid-viewport", "regular iOS snapshot presentation requires a valid viewport"),
            ("missing-viewport", "regular iOS snapshot presentation requires a viewport"),
            ("malformed-graph", "iOS snapshot graph contains an invalid node depth"),
            ("invalid-presented-payload", "regular iOS snapshot payload refers to a parent outside the payload"),
            ("invalid-presented-payload", "regular iOS snapshot payload marked a disabled or off-viewport node actionable"),
            ("invalid-quality-payload", "iOS snapshot graph contains an invalid node depth"),
        ];
        foreach (var (reason, text) in raised)
        {
            var translated = Engine(DeviceErrors.TranslateError(
                new AgentDeviceError("COMMAND_FAILED", text, Details(("reason", reason))),
                "snapshot",
                "session e2e-ios-0"));
            Assert.Equal("ENGINE_FAILURE", translated.Code);
            Assert.False(translated.Retryable);
            Assert.Contains($"could not present the accessibility snapshot (session e2e-ios-0): {text}", translated.Message, StringComparison.Ordinal);
            Assert.True(DeviceErrors.IsSnapshotPresentationFailure(translated), reason);
        }

        // A reason the presenter does not raise is not a presentation failure: agent-device tags a cancelled capture with one too.
        var cancelledCapture = DeviceErrors.TranslateError(
            new AgentDeviceError("COMMAND_FAILED", "snapshot cancelled", Details(("reason", "request_canceled"))),
            "snapshot");
        Assert.Equal("snapshot failed: snapshot cancelled", cancelledCapture.Message);
        Assert.False(DeviceErrors.IsSnapshotPresentationFailure(cancelledCapture));
    }
}
