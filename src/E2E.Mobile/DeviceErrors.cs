// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Mobile;

/// <summary>
/// Translation of agent-device failures onto the engine error contract.
/// <list type="table">
/// <listheader><term>agent-device failure</term><description>Code</description></listheader>
/// <item><term>already an engine, test, or configuration error</term><description>passed through untouched</description></item>
/// <item><term>cancellation</term><description>CANCELLED</description></item>
/// <item><term>no active session, session not found, requires a session</term><description>INVALID_STATE</description></item>
/// <item><term>UNSUPPORTED_OPERATION, NOT_IMPLEMENTED, UNSUPPORTED_PLATFORM, or "not supported on this device" under any code</term><description>UNSUPPORTED_CAPABILITY</description></item>
/// <item><term>a stale-ref refusal, by <c>details.reason</c> (action paths only)</term><description>NODE_STALE (retryable)</description></item>
/// <item><term>the iOS runner busy, wedged, or past its watchdog; a snapshot the runner acquired but could not present</term><description>ENGINE_FAILURE, naming the runner and the recovery</description></item>
/// <item><term>anything else, agent-device's own timeouts included</term><description>ENGINE_FAILURE with the text</description></item>
/// </list>
/// <c>OPERATION_TIMEOUT</c> is never produced here. In the contract it means the harness budget expired,
/// which <see cref="RunCommandAsync{T}"/> already reports as cancelled, and core replaces its message with
/// a bare <c>operation timed out</c>. A timeout agent-device itself reports (a simulator boot, the runner
/// connecting, an app launch) is a device-side fault whose text is the diagnosis, so it stays
/// <c>ENGINE_FAILURE</c> and keeps its message and hint.
/// </summary>
public static partial class DeviceErrors
{
    private const string PresentationCode = "IOS_SNAPSHOT_ENGINE_FAILED";

    private const string RunnerRecovery =
        "If the next run meets it again, stop the daemon (`npx agent-device daemon stop`) and reboot the simulator (`xcrun simctl shutdown <udid>`, then `boot`).";

    private static readonly HashSet<string> UnsupportedCodes = ["UNSUPPORTED_OPERATION", "NOT_IMPLEMENTED", "UNSUPPORTED_PLATFORM"];

    /// <summary>
    /// The <c>details.reason</c> values agent-device refuses an <c>@ref</c> with: the ref names no node of
    /// the session's tree, or belongs to a capture the session has since moved past (a newer snapshot, or
    /// an action that expired its refs). agent-device raises each one while resolving or admitting the
    /// ref, before the action reaches the device.
    /// </summary>
    private static readonly HashSet<string> StaleRefReasons =
    [
        "ref_not_found",
        "ref_frame_expired",
        "ref_generation_mismatch",
        "plain_ref_requires_complete_frame",
        "ref_not_issued",
    ];

    /// <summary>
    /// What the iOS automation runner reported about itself, by the code agent-device emits for it:
    /// <c>RUNNER_WEDGED</c> at the top level, <c>RUNNER_BUSY</c> and <c>MAIN_THREAD_TIMEOUT</c> in
    /// <c>details.runnerErrorCode</c> under <c>COMMAND_FAILED</c>, the shape the daemon folds runner wire
    /// codes into. Each value is the headline and the first move; the app is fine in every case.
    /// </summary>
    private static readonly Dictionary<string, RunnerStateText> RunnerStates = new(StringComparer.Ordinal)
    {
        ["RUNNER_BUSY"] = new("the iOS automation runner is still finishing a command that overran its watchdog", "Wait a few seconds and rerun."),
        ["MAIN_THREAD_TIMEOUT"] = new("the iOS automation runner's main thread overran its watchdog on this command", "Rerun once it has drained."),
        ["RUNNER_WEDGED"] = new("the iOS automation runner is wedged: its main thread is stuck in abandoned work", "agent-device restarts the runner; rerun."),
    };

    /// <summary>
    /// The reasons of a snapshot the iOS runner acquired but could not present. Upstream raises it as
    /// <c>IOS_SNAPSHOT_ENGINE_FAILED</c> and rethrows it to the client as <c>COMMAND_FAILED</c> with the
    /// check that failed in <c>details.reason</c>: a per-capture presentation check, not a property of the
    /// app or the screen. The reasons are agent-device's (<c>ios-snapshot-runtime</c>,
    /// <c>runner-presentation</c>): the viewport checks, a malformed graph, which the presenter rewraps as
    /// <c>invalid-presented-payload</c> on the presented tree and <c>invalid-quality-payload</c> on the
    /// quality tree, and the payload checks it raises as <c>invalid-presented-payload</c> directly (a parent
    /// outside the payload, a disabled or off-viewport node marked actionable).
    /// </summary>
    private static readonly HashSet<string> PresentationReasons =
    [
        "invalid-viewport",
        "missing-viewport",
        "malformed-graph",
        "invalid-presented-payload",
        "invalid-quality-payload",
    ];

    private static readonly RunnerStateText Presentation =
        new("the iOS automation runner could not present the accessibility snapshot", "Rerun.");

    /// <summary>A failure's message in agent-device's normalized form.</summary>
    public static string Message(Exception cause) => AgentDeviceErrors.Normalize(cause).Message;

    /// <summary>
    /// Translates an unexpected agent-device error at the contract boundary. <paramref name="where"/> names
    /// the session and device the command ran under, for the messages whose recovery is per device.
    /// </summary>
    public static Exception TranslateError(Exception cause, string operation, string? where = null)
    {
        if (IsClassified(cause))
        {
            return cause;
        }

        if (cause is OperationCanceledException)
        {
            return DeviceSupport.Cancelled($"{operation} cancelled");
        }

        var normalized = AgentDeviceErrors.Normalize(cause);
        var text = $"{operation} failed: {WithHint(cause, normalized.Message)}";
        if (normalized.Code == "SESSION_NOT_FOUND" || NoSession().IsMatch(normalized.Message))
        {
            return new EngineException(EngineErrorCodes.InvalidState, text, retryable: false, cause);
        }

        if (UnsupportedCodes.Contains(normalized.Code) || Unsupported().IsMatch(normalized.Message))
        {
            return new EngineException(EngineErrorCodes.UnsupportedCapability, text, retryable: false, cause);
        }

        if (RunnerState(cause) is { } state)
        {
            return new EngineException(EngineErrorCodes.EngineFailure, RunnerMessage(operation, where, state, cause), retryable: false, cause);
        }

        if (IsPresentationFailure(cause))
        {
            return new EngineException(EngineErrorCodes.EngineFailure, RunnerMessage(operation, where, Presentation, cause), retryable: false, cause);
        }

        return new EngineException(EngineErrorCodes.EngineFailure, text, retryable: false, cause);
    }

    /// <summary>
    /// True for a translated failure of the iOS automation runner itself: busy, wedged, or past its
    /// watchdog. The runner, not the app, is what needs attention, so a caller that tolerates an app that
    /// did not open (warm-up) must not tolerate this.
    /// </summary>
    public static bool IsRunnerFailure(Exception? error) => error?.InnerException is { } cause && RunnerState(cause) is not null;

    /// <summary>
    /// True for a translated settings command agent-device refused because its session is on no app: the
    /// one refusal a foreground open of the pinned app answers.
    /// </summary>
    public static bool IsNoSessionApp(Exception? error) =>
        error?.InnerException is { } cause && NoSessionApp().IsMatch(AgentDeviceErrors.Normalize(cause).Message);

    /// <summary>
    /// True for a translated snapshot the iOS runner acquired but could not present. The check is per
    /// capture and the capture is a read, so one retry is safe and usually enough.
    /// </summary>
    public static bool IsSnapshotPresentationFailure(Exception? error) => error?.InnerException is { } cause && IsPresentationFailure(cause);

    /// <summary>
    /// Runs one agent-device command under a cancellation token and translates its failure onto the engine
    /// taxonomy: the one boundary every command crosses.
    /// </summary>
    public static async Task<T> RunCommandAsync<T>(string label, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken, string? where = null)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await work(cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception cause)
        {
            throw TranslateError(cause, label, where);
        }
    }

    /// <summary>
    /// Like <see cref="TranslateError"/>, but a ref agent-device refused as stale becomes retryable
    /// <c>NODE_STALE</c>: nothing was dispatched, so the harness may re-observe and re-resolve the node
    /// instead of failing the action. Keyed on the reason alone: agent-device marks a drag's stale ref
    /// <c>dispatched: 'unknown'</c> after Android blocking-dialog recovery, although it refuses it before
    /// the gesture.
    /// </summary>
    public static Exception StaleOr(Exception cause, string operation, string? where = null)
    {
        if (IsClassified(cause))
        {
            return cause;
        }

        if (Details(cause).GetValueOrDefault("reason") is string reason && StaleRefReasons.Contains(reason))
        {
            return new EngineException(EngineErrorCodes.NodeStale, $"{operation}: {Message(cause)}", retryable: true, cause);
        }

        return TranslateError(cause, operation, where);
    }

    /// <summary>
    /// True for an error that already carries its classification: an engine, test, or configuration error.
    /// Those cross the boundary untouched; re-wrapping one would turn a <c>POLICY_DENIED</c> into an
    /// infrastructure failure.
    /// </summary>
    private static bool IsClassified(Exception cause) => cause is E2EException;

    /// <summary>The details bag the failure itself declared; the normalizer lifts some keys off it, so it is read raw.</summary>
    private static IReadOnlyDictionary<string, object?> Details(Exception cause) =>
        cause is AgentDeviceError { Details: { } details } ? details : new Dictionary<string, object?>();

    /// <summary>
    /// The normalized message followed by the hint the failure itself declared. That hint is the recovery
    /// path (press return, tap the app's own Done control) and the model only ever sees the error text, so
    /// dropping it leaves the agent retrying the same refused action. The per-code default hint the
    /// normalizer would add is CLI advice and stays out, and so does agent-device's generic hint.
    /// </summary>
    private static string WithHint(Exception cause, string text)
    {
        if (Details(cause).GetValueOrDefault("hint") is not string hint || hint.Trim().Length == 0 || CliAdvice().IsMatch(hint))
        {
            return text;
        }

        return $"{text} Hint: {hint.Trim()}";
    }

    /// <summary>The runner-state entry for a failure, by top-level code or the folded runner wire code.</summary>
    private static RunnerStateText? RunnerState(Exception cause)
    {
        if (RunnerStates.TryGetValue(AgentDeviceErrors.Normalize(cause).Code, out var byCode))
        {
            return byCode;
        }

        return Details(cause).GetValueOrDefault("runnerErrorCode") is string wire && RunnerStates.TryGetValue(wire, out var byWire) ? byWire : null;
    }

    /// <summary>True when the iOS runner acquired a snapshot and failed its own presentation check on it.</summary>
    private static bool IsPresentationFailure(Exception cause)
    {
        if (AgentDeviceErrors.Normalize(cause).Code == PresentationCode)
        {
            return true;
        }

        return Details(cause).GetValueOrDefault("reason") is string reason && PresentationReasons.Contains(reason);
    }

    /// <summary>
    /// The message for a fault of the automation runner itself: what the runner is doing, where (the
    /// session and device when the caller knows them), agent-device's own text, then the recovery. The
    /// harness reports an engine fault under the agent as <c>APP_UNREACHABLE</c>, so the message has to say
    /// the app is not the part that failed.
    /// </summary>
    private static string RunnerMessage(string operation, string? where, RunnerStateText state, Exception cause)
    {
        var location = where is null ? "" : $" ({where})";
        return $"{operation} failed: {state.Headline}{location}: {Message(cause)} The app is fine. {state.FirstMove} {RunnerRecovery}";
    }

    [GeneratedRegex(
        @"no active (?:app )?session|requires an active session|session\b.*\bnot found|open an app first|no app (?:is )?open",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoSession();

    [GeneratedRegex(@"\b(?:is )?not supported\b|\bunsupported\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Unsupported();

    [GeneratedRegex(@"requires an active app in (?:the )?session", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex NoSessionApp();

    /// <summary>agent-device's generic hint (<c>Check command arguments and run --help</c>) is CLI advice.</summary>
    [GeneratedRegex(@"--help|command arguments", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CliAdvice();

    private sealed record RunnerStateText(string Headline, string FirstMove);
}
