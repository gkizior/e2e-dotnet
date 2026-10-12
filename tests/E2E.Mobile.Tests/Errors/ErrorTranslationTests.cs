// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Mobile.Tests.Errors;

public sealed class ErrorTranslationTests
{
    private static EngineException Engine(Exception error) => Assert.IsType<EngineException>(error);

    private static Dictionary<string, object?> Details(params (string Key, object? Value)[] details) =>
        details.ToDictionary(detail => detail.Key, detail => detail.Value);

    [Fact]
    public void Passes_classified_errors_through_untouched()
    {
        var engine = new EngineException("NOT_ACTIONABLE", "no", retryable: false);
        Assert.Same(engine, DeviceErrors.TranslateError(engine, "perform"));
        var runner = new TestException("POLICY_DENIED", "no");
        Assert.Same(runner, DeviceErrors.StaleOr(runner, "perform"));
    }

    [Fact]
    public void Maps_a_missing_session_to_INVALID_STATE_and_unsupported_operations_to_UNSUPPORTED_CAPABILITY()
    {
        var missing = Engine(DeviceErrors.TranslateError(new AgentDeviceError("SESSION_NOT_FOUND", "session gone"), "snapshot"));
        Assert.Equal("INVALID_STATE", missing.Code);
        Assert.False(missing.Retryable);

        // What `appstate` says once `close` ended the session; the harness reports it as APP_NOT_OPEN.
        var closed = Engine(DeviceErrors.TranslateError(
            new AgentDeviceError(
                "INVALID_ARGS",
                "appstate requires an active session or an explicit device selector (e.g. --platform ios).",
                Details(("reason", "session_or_device_selector_required"), ("dispatched", "no"))),
            "device.foregroundApp"));
        Assert.Equal("INVALID_STATE", closed.Code);
        Assert.False(closed.Retryable);

        // Keyed on the code and reason, never the text: a message that only reads like a missing session stays an engine failure.
        Assert.Equal(
            "ENGINE_FAILURE",
            Engine(DeviceErrors.TranslateError(new AgentDeviceError("INVALID_ARGS", "appstate requires an active session"), "device.foregroundApp")).Code);
        Assert.Equal(
            "INVALID_STATE",
            Engine(DeviceErrors.TranslateError(
                new AgentDeviceError("SESSION_NOT_FOUND", "iOS appstate requires an active session on the target device. Run open first."),
                "device.foregroundApp")).Code);
        Assert.Equal(
            "UNSUPPORTED_CAPABILITY",
            Engine(DeviceErrors.TranslateError(new AgentDeviceError("UNSUPPORTED_OPERATION", "hover is macOS only"), "perform hover")).Code);
        Assert.Equal(
            "UNSUPPORTED_CAPABILITY",
            Engine(DeviceErrors.TranslateError(
                new AgentDeviceError("COMMAND_FAILED", "Android shell clipboard write is not supported on this device."),
                "device.setClipboard")).Code);
    }

    [Fact]
    public void Keeps_a_timeout_the_device_reports_under_ENGINE_FAILURE_with_its_text_and_hint_never_OPERATION_TIMEOUT()
    {
        const string hint = "Retry simulator boot and inspect simctl bootstatus logs; in CI reduce parallel jobs or use a larger runner.";
        var bootTimeout = Engine(DeviceErrors.TranslateError(
            new AgentDeviceError("IOS_BOOT_TIMEOUT", "Timed out waiting for iPhone 17 Pro to boot", Details(("hint", hint))),
            "boot"));
        Assert.Equal("ENGINE_FAILURE", bootTimeout.Code);
        Assert.False(bootTimeout.Retryable);
        Assert.Equal($"boot failed: Timed out waiting for iPhone 17 Pro to boot Hint: {hint}", bootTimeout.Message);
        var snapshot = Engine(DeviceErrors.TranslateError(new Exception("snapshot timed out after 30000ms"), "snapshot"));
        Assert.Equal("ENGINE_FAILURE", snapshot.Code);
        Assert.Equal("snapshot failed: snapshot timed out after 30000ms", snapshot.Message);
        var connect = Engine(DeviceErrors.TranslateError(
            new AgentDeviceError("IOS_RUNNER_CONNECT_TIMEOUT", "runner connect timed out after 60s"),
            "open com.example.app"));
        Assert.Equal("ENGINE_FAILURE", connect.Code);
        Assert.Equal("open com.example.app failed: runner connect timed out after 60s", connect.Message);
    }

    // Each refusal as agent-device 0.21.21 sends it: the typed reason, and the message the reason replaces.
    [Theory]
    [InlineData("ref_not_found", "Ref @e12 not found", "no")]
    [InlineData("ref_frame_expired", "Ref @e12 belongs to an expired ref frame — a device action since the snapshot invalidated it", "no")]
    [InlineData("ref_generation_mismatch", "Ref @e12 was minted from a superseded snapshot generation", "no")]
    [InlineData("plain_ref_requires_complete_frame", "Ref @e12 needs a complete snapshot — the current frame only authorizes its emitted refs", "no")]
    [InlineData("ref_not_issued", "Ref @e12 was not issued by the current ref frame", "no")]

    // After Android blocking-dialog recovery, a drag refuses its stale ref before the gesture but reports it as `unknown`.
    [InlineData("ref_frame_expired", "Ref @e12 belongs to an expired ref frame — a device action since the snapshot invalidated it", "unknown")]
    public void Maps_the_stale_ref_refusal_to_retryable_NODE_STALE(string reason, string text, string dispatched)
    {
        var refused = new AgentDeviceError("COMMAND_FAILED", text, Details(("reason", reason), ("dispatched", dispatched)));
        var stale = Engine(DeviceErrors.StaleOr(refused, "perform tap"));
        Assert.Equal("NODE_STALE", stale.Code);
        Assert.True(stale.Retryable);
        Assert.Equal($"perform tap: {text}", stale.Message);

        // Outside an action path the same refusal stays an engine failure.
        Assert.Equal("ENGINE_FAILURE", Engine(DeviceErrors.TranslateError(refused, "observe")).Code);
    }

    [Fact]
    public void Maps_the_rest_to_ENGINE_FAILURE_with_the_text_a_ref_named_in_a_message_without_a_stale_reason_included()
    {
        var named = Engine(DeviceErrors.StaleOr(new Exception("ref @e12 not found in the current snapshot"), "perform tap"));
        Assert.Equal("ENGINE_FAILURE", named.Code);
        Assert.False(named.Retryable);
        var failure = Engine(DeviceErrors.TranslateError(new AgentDeviceError("COMMAND_FAILED", "xcrun exploded"), "boot"));
        Assert.Equal("ENGINE_FAILURE", failure.Code);
        Assert.False(failure.Retryable);
        Assert.Equal("boot failed: xcrun exploded", failure.Message);
    }

    [Fact]
    public void Keeps_the_agent_device_hint_in_the_message_so_the_model_sees_the_recovery_path()
    {
        var refused = new AgentDeviceError(
            "UNSUPPORTED_OPERATION",
            "Unable to dismiss the iOS keyboard: the keyboard exposes no dismiss key",
            Details(("hint", "Tap the app's own Done control, or press the return key to submit.")));
        var translated = Engine(DeviceErrors.TranslateError(refused, "keyboard.dismiss"));
        Assert.Equal("UNSUPPORTED_CAPABILITY", translated.Code);
        Assert.Equal(
            "keyboard.dismiss failed: Unable to dismiss the iOS keyboard: the keyboard exposes no dismiss key Hint: Tap the app's own Done control, or press the return key to submit.",
            translated.Message);
        Assert.Equal("boot failed: plain", DeviceErrors.TranslateError(new AgentDeviceError("COMMAND_FAILED", "plain"), "boot").Message);
    }

    [Fact]
    public void Drops_agent_devices_generic_CLI_advice_which_helps_neither_the_model_nor_the_run_page()
    {
        var advised = new AgentDeviceError(
            "COMMAND_FAILED",
            "permission setting requires an active app in session",
            Details(("hint", "Check command arguments and run --help for usage examples.")));
        Assert.Equal(
            "device.setPermission failed: permission setting requires an active app in session",
            DeviceErrors.TranslateError(advised, "device.setPermission").Message);
    }

    [Fact]
    public void Turns_an_AbortError_into_CANCELLED()
    {
        Assert.Equal("CANCELLED", Engine(DeviceErrors.TranslateError(new OperationCanceledException("aborted"), "snapshot")).Code);
    }
}
