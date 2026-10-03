// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;
using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>One goal (<see cref="ActAsync"/>) or a check against the current screen.</summary>
public sealed class Agent
{
    private const string ActSystem =
        "You are an e2e test agent. Act only by calling tools. Finish with done. " +
        "Target a control by its role and name from the screen snapshot. " +
        "done status is passed, failed, or blocked. " +
        "Never type a secret value. Call fill_secret with the secret name. " +
        "A secret looks like <secret:name>.";

    private const string JudgeSystem =
        "You judge one statement against the current screen. Call done. " +
        "status passed when the screen shows the statement is true. " +
        "status failed with code ASSERTION_FAILED when the statement is false. " +
        "status failed with code ASSERTION_INCONCLUSIVE when the screen does not show enough to decide. " +
        "You do not see earlier steps. Do not call any tool except done.";

    private readonly AttemptScope _scope;

    internal Agent(AttemptScope scope)
    {
        _scope = scope;
    }

    public async Task<ActResult> ActAsync(string instruction, ActOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        using var linked = Link(cancellationToken, options?.Timeout ?? _scope.StepTimeout);
        var token = linked.Token;
        _scope.Remember(options?.Params);
        var key = CacheKeys.Create(_scope.EnginePlatform, _scope.EngineVersion, _scope.TestTitle, instruction, options?.Params);
        var pending = new PendingAct { Key = key, ParamCollision = CacheKeys.Collides(options?.Params) };
        if (_scope.CacheEnabled)
        {
            _scope.Acts.Add(pending);
        }

        var start = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
        var actions = new List<RecordedAction>();
        CacheInfo? info = null;
        var handoff = false;
        if (_scope.CacheEnabled && _scope.Attempt == 1 && _scope.Cache is not null)
        {
            var replay = await TryReplayAsync(key, start, actions, options?.Params, token).ConfigureAwait(false);
            info = replay.Info;
            handoff = replay.Handoff;
            if (replay.Completed)
            {
                pending.Completed = true;
                pending.Entry = BuildEntry(instruction, start, await _scope.Session.ObserveAsync(token).ConfigureAwait(false), actions, options?.Params);
                _scope.Completed.Add("Replayed: " + instruction);
                return new ActResult { Summary = "Replayed recorded actions.", Cache = info };
            }
        }

        var maxCalls = options?.MaxModelCalls ?? _scope.MaxModelCalls;
        var messages = new List<ModelMessage>
        {
            new()
            {
                Role = "user",
                Content = Opening(instruction, options?.Params, start, actions, handoff ? info?.Reason : null),
            },
        };

        var failures = 0;
        string? summary = null;
        for (var call = 0; call < maxCalls; call++)
        {
            if (failures >= 5)
            {
                messages.Add(new ModelMessage { Role = "user", Content = "Stop acting. Call done with a verdict." });
            }

            var response = await CallModelAsync(ActSystem, messages, AgentTools.Act, token).ConfigureAwait(false);
            if (response.ToolCalls.Count == 0)
            {
                messages.Add(new ModelMessage { Role = "assistant", Content = response.Content });
                messages.Add(new ModelMessage { Role = "user", Content = "Call a tool. End the step with done." });
                failures++;
                continue;
            }

            messages.Add(new ModelMessage { Role = "assistant", Content = response.Content, ToolCalls = response.ToolCalls });
            foreach (var toolCall in response.ToolCalls)
            {
                var outcome = await ExecuteAsync(toolCall, options?.Params, actions, token).ConfigureAwait(false);
                messages.Add(new ModelMessage
                {
                    Role = "tool",
                    ToolCallId = toolCall.Id,
                    Name = toolCall.Name,
                    Content = SnapshotText.Redact(outcome.Content, _scope.Secrets),
                });
                if (!outcome.Succeeded)
                {
                    failures++;
                }

                if (!outcome.Done)
                {
                    continue;
                }

                summary = outcome.Summary ?? "";
                var code = outcome.Code;
                if (string.Equals(outcome.Status, "passed", StringComparison.Ordinal))
                {
                    pending.Completed = true;
                    var end = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
                    pending.Entry = BuildEntry(instruction, start, end, actions, options?.Params);
                    _scope.Completed.Add(summary.Length == 0 ? instruction : summary);
                    return new ActResult { Summary = summary, Cache = info };
                }

                if (string.Equals(outcome.Status, "blocked", StringComparison.Ordinal))
                {
                    throw new AgentException(code ?? "AUTOMATION_UNSUPPORTED", summary.Length == 0 ? "The step is blocked." : summary);
                }

                throw new AgentException(code ?? "ASSERTION_FAILED", summary.Length == 0 ? "The step failed." : summary);
            }

            if (failures >= 3 && failures < 5)
            {
                messages.Add(new ModelMessage { Role = "user", Content = "The last actions failed. Change approach, then call done if you cannot." });
            }
        }

        throw new AgentException("STEP_BUDGET_EXHAUSTED", "The step used its model-call budget.");
    }

    public Task AssertAsync(string statement, AssertOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        return JudgeAsync(statement, options?.Timeout ?? _scope.StepTimeout, cancellationToken, verify: true);
    }

    public async Task WaitForAsync(string statement, WaitForOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        var timeout = options?.Timeout ?? _scope.StepTimeout;
        var interval = options?.Interval ?? TimeSpan.FromMilliseconds(200);
        using var linked = Link(cancellationToken, timeout);
        var token = linked.Token;
        var deadline = DateTime.UtcNow + timeout;
        string? last = null;
        var calls = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var observation = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
            var snapshot = SnapshotText.Render(observation, _scope.Secrets);
            if (!string.Equals(snapshot, last, StringComparison.Ordinal))
            {
                last = snapshot;
                calls++;
                if (calls > _scope.MaxModelCalls)
                {
                    throw new AgentException("STEP_BUDGET_EXHAUSTED", "waitFor used its model-call budget.");
                }

                var verdict = await JudgeOnceAsync(statement, snapshot, token).ConfigureAwait(false);
                if (string.Equals(verdict.Status, "passed", StringComparison.Ordinal))
                {
                    _scope.MarkVerified();
                    return;
                }

                if (string.Equals(verdict.Status, "blocked", StringComparison.Ordinal)
                    && verdict.Code is not ("ASSERTION_FAILED" or "ASSERTION_INCONCLUSIVE"))
                {
                    throw new AgentException(verdict.Code ?? "AUTOMATION_UNSUPPORTED", verdict.Summary ?? "waitFor is blocked.");
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new AgentException("STEP_TIMEOUT", "waitFor timed out before the screen matched.");
            }

            await Task.Delay(interval, token).ConfigureAwait(false);
        }
    }

    public async Task<T> ExtractAsync<T>(string instruction, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        using var linked = Link(cancellationToken, _scope.StepTimeout);
        var token = linked.Token;
        var observation = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
        var snapshot = SnapshotText.Render(observation, _scope.Secrets);
        var shape = ShapeOf(typeof(T));
        var messages = new List<ModelMessage>
        {
            new()
            {
                Role = "user",
                Content = "Instruction: " + instruction + "\n\nReturn JSON with this shape: " + shape + "\n\n" + snapshot,
            },
        };
        var response = await CallModelAsync(JudgeSystem, messages, AgentTools.Extract, token).ConfigureAwait(false);
        var call = response.ToolCalls.FirstOrDefault(item => string.Equals(item.Name, "extract", StringComparison.Ordinal));
        if (call is null)
        {
            messages.Add(new ModelMessage { Role = "assistant", Content = response.Content, ToolCalls = response.ToolCalls });
            messages.Add(new ModelMessage { Role = "user", Content = "Call extract with the data. If it is not on screen, call done with status failed and code ASSERTION_INCONCLUSIVE." });
            response = await CallModelAsync(JudgeSystem, messages, AgentTools.Extract, token).ConfigureAwait(false);
            call = response.ToolCalls.FirstOrDefault(item => string.Equals(item.Name, "extract", StringComparison.Ordinal));
        }

        if (call is null)
        {
            var done = response.ToolCalls.FirstOrDefault(item => string.Equals(item.Name, "done", StringComparison.Ordinal));
            if (done is not null)
            {
                throw new AgentException(Args.String(done.Arguments, "code") ?? "ASSERTION_INCONCLUSIVE", Args.String(done.Arguments, "summary") ?? "The screen did not contain the data.");
            }

            throw new AgentException("MODEL_OUTPUT_INVALID", "extract did not return data.");
        }

        if (!call.Arguments.TryGetProperty("data", out var data) && !TryProperty(call.Arguments, "data", out data))
        {
            throw new AgentException("ASSERTION_INCONCLUSIVE", "The model did not return data for this extract.");
        }

        try
        {
            var value = data.Deserialize<T>(JsonDefaults.Options);
            if (value is null)
            {
                throw new AgentException("ASSERTION_INCONCLUSIVE", "The model returned empty data.");
            }

            return value;
        }
        catch (JsonException ex)
        {
            throw new AgentException("MODEL_OUTPUT_INVALID", "extract data did not match the requested shape.", ex);
        }
    }

    private async Task JudgeAsync(string statement, TimeSpan timeout, CancellationToken cancellationToken, bool verify)
    {
        using var linked = Link(cancellationToken, timeout);
        var token = linked.Token;
        var observation = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
        var verdict = await JudgeOnceAsync(statement, SnapshotText.Render(observation, _scope.Secrets), token).ConfigureAwait(false);
        if (string.Equals(verdict.Status, "passed", StringComparison.Ordinal))
        {
            if (verify)
            {
                _scope.MarkVerified();
            }

            return;
        }

        var code = verdict.Code;
        if (string.Equals(verdict.Status, "failed", StringComparison.Ordinal) && string.IsNullOrEmpty(code))
        {
            code = "ASSERTION_FAILED";
        }

        if (string.Equals(verdict.Status, "blocked", StringComparison.Ordinal) && string.IsNullOrEmpty(code))
        {
            code = "AUTOMATION_UNSUPPORTED";
        }

        throw new AgentException(code ?? "ASSERTION_FAILED", verdict.Summary ?? "The statement did not hold.");
    }

    private async Task<Verdict> JudgeOnceAsync(string statement, string snapshot, CancellationToken token)
    {
        var messages = new List<ModelMessage>
        {
            new() { Role = "user", Content = "Statement: " + statement + "\n\n" + snapshot },
        };
        var response = await CallModelAsync(JudgeSystem, messages, AgentTools.Judge, token).ConfigureAwait(false);
        var verdict = ReadVerdict(response);
        if (verdict is not null)
        {
            return verdict.Value;
        }

        messages.Add(new ModelMessage { Role = "assistant", Content = response.Content, ToolCalls = response.ToolCalls });
        messages.Add(new ModelMessage { Role = "user", Content = "Call done with status passed, failed, or blocked." });
        response = await CallModelAsync(JudgeSystem, messages, AgentTools.Judge, token).ConfigureAwait(false);
        verdict = ReadVerdict(response);
        if (verdict is null)
        {
            throw new AgentException("MODEL_OUTPUT_INVALID", "The judge did not call done.");
        }

        return verdict.Value;
    }

    private static Verdict? ReadVerdict(ModelResponse response)
    {
        var done = response.ToolCalls.FirstOrDefault(call => string.Equals(call.Name, "done", StringComparison.Ordinal));
        if (done is null)
        {
            return null;
        }

        var status = Args.String(done.Arguments, "status");
        if (status is not ("passed" or "failed" or "blocked"))
        {
            return null;
        }

        var code = Args.String(done.Arguments, "code");
        if (string.Equals(status, "passed", StringComparison.Ordinal))
        {
            code = null;
        }

        return new Verdict(status, Args.String(done.Arguments, "summary"), code);
    }

    private async Task<ReplayAttempt> TryReplayAsync(
        string key,
        Observation start,
        List<RecordedAction> actions,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken token)
    {
        var lookup = _scope.Cache!.Read(key);
        if (lookup.Entry is null)
        {
            _scope.Missed++;
            return ReplayAttempt.Miss(lookup.Reason ?? "no-entry");
        }

        var entry = lookup.Entry;
        if (!string.Equals(entry.Route, start.Route, StringComparison.Ordinal))
        {
            _scope.Missed++;
            return ReplayAttempt.Miss("wrong-context");
        }

        if (entry.Actions.Count == 0)
        {
            _scope.Missed++;
            return ReplayAttempt.Miss("invalid-entry");
        }

        var started = false;
        foreach (var action in entry.Actions)
        {
            if (string.Equals(action.Kind, "navigate", StringComparison.Ordinal))
            {
                await _scope.Session.OpenAsync(action.Url ?? "/", token).ConfigureAwait(false);
                actions.Add(action);
                started = true;
                continue;
            }

            var found = await WaitForTargetAsync(action, token).ConfigureAwait(false);
            if (found.Node is null)
            {
                if (!started)
                {
                    _scope.Missed++;
                    actions.Clear();
                    return ReplayAttempt.Miss(found.Reason ?? "target-not-found");
                }

                _scope.HandedOff++;
                return ReplayAttempt.Hand(found.Reason ?? "target-not-found");
            }

            try
            {
                var performed = ResolveSecret(Detemplate(action, parameters));
                await PerformRecordedAsync(found.Node, performed, token).ConfigureAwait(false);
                actions.Add(action);
                started = true;
            }
            catch (E2EException)
            {
                _scope.HandedOff++;
                return ReplayAttempt.Hand("action-failed");
            }
        }

        if (!await WaitForEndAsync(entry, token).ConfigureAwait(false))
        {
            _scope.HandedOff++;
            return ReplayAttempt.Hand("end-mismatch");
        }

        _scope.Replayed++;
        return ReplayAttempt.Done();
    }

    // The last action may start a navigation or a slow render, so the end route and anchors get the action timeout to show up.
    private async Task<bool> WaitForEndAsync(CacheEntry entry, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + _scope.ActionTimeout;
        while (true)
        {
            var end = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
            if (string.Equals(entry.EndRoute, end.Route, StringComparison.Ordinal)
                && entry.Appeared.All(appeared => Find(end, appeared.Role, appeared.Name, appeared.TestId, null).Count == 1))
            {
                return true;
            }

            if (DateTime.UtcNow >= deadline)
            {
                return false;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), token).ConfigureAwait(false);
        }
    }

    private async Task<(SemanticNode? Node, string? Reason)> WaitForTargetAsync(RecordedAction action, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + _scope.ActionTimeout;
        while (true)
        {
            var observation = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
            var matches = Find(observation, action.Role, action.Name, action.TestId, null);
            if (matches.Count == 1)
            {
                return (matches[0], null);
            }

            if (matches.Count > 1)
            {
                return (null, "target-ambiguous");
            }

            if (DateTime.UtcNow >= deadline)
            {
                return (null, "target-not-found");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), token).ConfigureAwait(false);
        }
    }

    private async Task PerformRecordedAsync(SemanticNode node, RecordedAction action, CancellationToken token)
    {
        LocatorAction locatorAction = action.Kind switch
        {
            "tap" => new LocatorAction.Tap(),
            "fill" => new LocatorAction.Fill(action.Value ?? "", false),
            "press" => new LocatorAction.Press(action.Key ?? "Enter"),
            "select" => new LocatorAction.Select(action.Value ?? ""),
            "check" => new LocatorAction.Check(),
            "uncheck" => new LocatorAction.Uncheck(),
            "clear" => new LocatorAction.Clear(),
            _ => throw new AgentException("AUTOMATION_UNSUPPORTED", "Cannot replay " + action.Kind + "."),
        };
        await _scope.Session.PerformAsync(node, locatorAction, token).ConfigureAwait(false);
    }

    private async Task<ToolOutcome> ExecuteAsync(
        ModelToolCall call,
        IReadOnlyDictionary<string, object?>? parameters,
        List<RecordedAction> actions,
        CancellationToken token)
    {
        if (string.Equals(call.Name, "done", StringComparison.Ordinal))
        {
            var status = Args.String(call.Arguments, "status");
            if (status is not ("passed" or "failed" or "blocked"))
            {
                return ToolOutcome.Fail("done status must be passed, failed, or blocked.");
            }

            var code = Args.String(call.Arguments, "code");
            if (string.Equals(status, "passed", StringComparison.Ordinal))
            {
                code = null;
            }

            return ToolOutcome.Finish(status, Args.String(call.Arguments, "summary"), code);
        }

        if (string.Equals(call.Name, "navigate", StringComparison.Ordinal))
        {
            var url = Args.String(call.Arguments, "url") ?? "/";
            await _scope.Session.OpenAsync(url, token).ConfigureAwait(false);
            actions.Add(new RecordedAction { Kind = "navigate", Url = url });
            return ToolOutcome.Ok(await DescribeAsync("navigated to " + Routes.PathOf(url), token).ConfigureAwait(false));
        }

        if (string.Equals(call.Name, "press", StringComparison.Ordinal) && Args.String(call.Arguments, "role") is null && Args.String(call.Arguments, "name") is null && Args.String(call.Arguments, "ref") is null)
        {
            var key = Args.String(call.Arguments, "key") ?? "Enter";
            try
            {
                await _scope.Session.PressAsync(key, token).ConfigureAwait(false);
            }
            catch (E2EException ex)
            {
                return ToolOutcome.Fail(ex.Message);
            }

            actions.Add(new RecordedAction { Kind = "press", Key = key });
            return ToolOutcome.Ok(await DescribeAsync("pressed " + key, token).ConfigureAwait(false));
        }

        SemanticNode node;
        try
        {
            node = await ResolveAsync(call.Arguments, token).ConfigureAwait(false);
        }
        catch (TestException ex)
        {
            return ToolOutcome.Fail(ex.Message);
        }

        try
        {
            switch (call.Name)
            {
                case "tap":
                    await _scope.Session.PerformAsync(node, new LocatorAction.Tap(), token).ConfigureAwait(false);
                    actions.Add(Record(node, "tap"));
                    return ToolOutcome.Ok(await DescribeAsync("tapped " + Label(node), token).ConfigureAwait(false));
                case "fill":
                    var value = Args.String(call.Arguments, "value") ?? "";
                    await _scope.Session.PerformAsync(node, new LocatorAction.Fill(value, false), token).ConfigureAwait(false);
                    actions.Add(new RecordedAction
                    {
                        Kind = "fill",
                        Role = node.Role,
                        Name = node.Name,
                        TestId = node.TestId,
                        Value = CacheKeys.Template(value, parameters),
                    });
                    return ToolOutcome.Ok(await DescribeAsync("filled " + Label(node), token).ConfigureAwait(false));
                case "fill_secret":
                    return await FillSecretAsync(node, call.Arguments, actions, token).ConfigureAwait(false);
                case "press":
                    var key = Args.String(call.Arguments, "key") ?? "Enter";
                    await _scope.Session.PerformAsync(node, new LocatorAction.Press(key), token).ConfigureAwait(false);
                    actions.Add(new RecordedAction { Kind = "press", Role = node.Role, Name = node.Name, TestId = node.TestId, Key = key });
                    return ToolOutcome.Ok(await DescribeAsync("pressed " + key + " on " + Label(node), token).ConfigureAwait(false));
                case "select":
                    var selected = Args.String(call.Arguments, "value") ?? "";
                    await _scope.Session.PerformAsync(node, new LocatorAction.Select(selected), token).ConfigureAwait(false);
                    actions.Add(new RecordedAction { Kind = "select", Role = node.Role, Name = node.Name, TestId = node.TestId, Value = selected });
                    return ToolOutcome.Ok(await DescribeAsync("selected " + selected, token).ConfigureAwait(false));
                case "check":
                    await _scope.Session.PerformAsync(node, new LocatorAction.Check(), token).ConfigureAwait(false);
                    actions.Add(Record(node, "check"));
                    return ToolOutcome.Ok(await DescribeAsync("checked " + Label(node), token).ConfigureAwait(false));
                case "uncheck":
                    await _scope.Session.PerformAsync(node, new LocatorAction.Uncheck(), token).ConfigureAwait(false);
                    actions.Add(Record(node, "uncheck"));
                    return ToolOutcome.Ok(await DescribeAsync("unchecked " + Label(node), token).ConfigureAwait(false));
                case "clear":
                    await _scope.Session.PerformAsync(node, new LocatorAction.Clear(), token).ConfigureAwait(false);
                    actions.Add(Record(node, "clear"));
                    return ToolOutcome.Ok(await DescribeAsync("cleared " + Label(node), token).ConfigureAwait(false));
                default:
                    return ToolOutcome.Fail("Unknown tool " + call.Name + ".");
            }
        }
        catch (E2EException ex)
        {
            return ToolOutcome.Fail(ex.Message);
        }
    }

    private async Task<ToolOutcome> FillSecretAsync(SemanticNode node, JsonElement arguments, List<RecordedAction> actions, CancellationToken token)
    {
        var name = Args.String(arguments, "secret");
        var secret = _scope.Secrets.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (secret is null)
        {
            return ToolOutcome.Fail("No secret named '" + (name ?? "") + "' was passed to this step.");
        }

        var purpose = node.InputPurpose;
        var allowed = node.States.Secure || purpose is "password" or "one-time-code" or "generic-secret";
        if (!allowed)
        {
            return ToolOutcome.Fail("Secret fill refused: " + Label(node) + " is not a secret field.");
        }

        await _scope.Session.PerformAsync(node, new LocatorAction.Fill(secret.Value, true), token).ConfigureAwait(false);
        actions.Add(new RecordedAction
        {
            Kind = "fill",
            Role = node.Role,
            Name = node.Name,
            TestId = node.TestId,
            Value = "<secret:" + secret.Name + ">",
        });
        return ToolOutcome.Ok(await DescribeAsync("filled secret <secret:" + secret.Name + "> into " + Label(node), token).ConfigureAwait(false));
    }

    private async Task<SemanticNode> ResolveAsync(JsonElement arguments, CancellationToken token)
    {
        var observation = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
        var matches = Find(observation, Args.String(arguments, "role"), Args.String(arguments, "name"), Args.String(arguments, "testId"), Args.String(arguments, "ref"));
        if (matches.Count == 0)
        {
            throw new TestException("NOT_FOUND", "No control matched role=" + Args.String(arguments, "role") + " name=" + Args.String(arguments, "name") + ".");
        }

        if (matches.Count > 1)
        {
            throw new TestException("STRICT_MODE", "Matched " + matches.Count.ToString(CultureInfo.InvariantCulture) + " controls. Name the control more specifically.");
        }

        return matches[0];
    }

    private static List<SemanticNode> Find(Observation observation, string? role, string? name, string? testId, string? reference)
    {
        var matches = new List<SemanticNode>();
        foreach (var node in LocatorResolver.Walk(observation.Roots))
        {
            if (node.States.Hidden)
            {
                continue;
            }

            if (reference is not null && role is null && name is null && testId is null)
            {
                if (string.Equals(node.Ref, reference, StringComparison.Ordinal))
                {
                    matches.Add(node);
                }

                continue;
            }

            if (role is not null && !string.Equals(node.Role, role, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (name is not null && !TextRules.Matches(node.Name, name, exact: true))
            {
                continue;
            }

            if (testId is not null && !string.Equals(node.TestId, testId, StringComparison.Ordinal))
            {
                continue;
            }

            if (role is null && name is null && testId is null)
            {
                continue;
            }

            matches.Add(node);
        }

        return matches;
    }

    private async Task<string> DescribeAsync(string action, CancellationToken token)
    {
        var observation = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
        return action + "\n" + SnapshotText.Render(observation, _scope.Secrets);
    }

    private string Opening(
        string instruction,
        IReadOnlyDictionary<string, object?>? parameters,
        Observation start,
        IReadOnlyList<RecordedAction> already,
        string? handoffReason)
    {
        var builder = new StringBuilder();
        if (handoffReason is not null)
        {
            builder.Append("Replay stopped (").Append(handoffReason).Append(") after these actions:\n");
            foreach (var action in already)
            {
                builder.Append("- ").Append(action.Kind).Append(' ').Append(action.Role).Append(" \"").Append(action.Name).Append("\"\n");
            }

            builder.Append("Continue from the current screen. Do not repeat an action that already had its effect.\n\n");
        }

        builder.Append("Goal: ").Append(Interpolate(instruction, parameters)).Append('\n');
        if (parameters is { Count: > 0 })
        {
            builder.Append("\nParams:\n");
            foreach (var pair in parameters)
            {
                builder.Append("- ").Append(pair.Key).Append(": ").Append(CacheKeys.Display(pair.Value)).Append('\n');
            }
        }

        if (_scope.Completed.Count > 0)
        {
            builder.Append("\nCompleted steps:\n");
            foreach (var step in _scope.Completed)
            {
                builder.Append("- ").Append(step).Append('\n');
            }
        }

        builder.Append('\n').Append(SnapshotText.Render(start, _scope.Secrets));
        return SnapshotText.Redact(builder.ToString(), _scope.Secrets);
    }

    private async Task<ModelResponse> CallModelAsync(string system, List<ModelMessage> messages, IReadOnlyList<ModelTool> tools, CancellationToken token)
    {
        if (_scope.Model is null)
        {
            throw new AgentException("MODEL_UNAVAILABLE", "No model is configured. Agent steps need an IAgentModel, or a replay that finishes on its own.");
        }

        ModelResponse response;
        try
        {
            response = await _scope.Model.CompleteAsync(new ModelRequest { System = system, Messages = messages, Tools = tools }, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!_scope.Token().IsCancellationRequested)
        {
            throw new AgentException("STEP_TIMEOUT", "Agent step timed out.");
        }

        _scope.ModelCalls++;
        if (response.Usage is { } usage)
        {
            _scope.InputTokens += usage.InputTokens;
            _scope.OutputTokens += usage.OutputTokens;
        }

        return response;
    }

    private CacheEntry BuildEntry(
        string instruction,
        Observation start,
        Observation end,
        List<RecordedAction> actions,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        return new CacheEntry
        {
            Schema = FileStepCache.SchemaVersion,
            Test = _scope.TestTitle,
            Instruction = instruction.Trim(),
            Route = start.Route,
            EndRoute = end.Route,
            Actions = actions.ToList(),
            Appeared = Appeared(start, end, parameters),
        };
    }

    private static List<RecordedTarget> Appeared(Observation before, Observation after, IReadOnlyDictionary<string, object?>? parameters)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in LocatorResolver.Walk(before.Roots))
        {
            if (!node.States.Hidden && !string.IsNullOrEmpty(node.Name))
            {
                seen.Add((node.Role ?? "") + "\n" + node.Name + "\n" + node.TestId);
            }
        }

        var appeared = new List<RecordedTarget>();
        foreach (var node in LocatorResolver.Walk(after.Roots))
        {
            if (node.States.Hidden || string.IsNullOrEmpty(node.Name))
            {
                continue;
            }

            if (ContainsParam(node.Name, parameters))
            {
                continue;
            }

            var key = (node.Role ?? "") + "\n" + node.Name + "\n" + node.TestId;
            if (!seen.Add(key))
            {
                continue;
            }

            appeared.Add(new RecordedTarget { Role = node.Role, Name = node.Name, TestId = node.TestId });
            if (appeared.Count == 8)
            {
                break;
            }
        }

        return appeared;
    }

    private static bool ContainsParam(string name, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null)
        {
            return false;
        }

        foreach (var value in parameters.Values)
        {
            if (value is UniqueValue unique && unique.Value.Length > 0 && name.Contains(unique.Value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static RecordedAction Record(SemanticNode node, string kind)
    {
        return new RecordedAction { Kind = kind, Role = node.Role, Name = node.Name, TestId = node.TestId };
    }

    private static RecordedAction Detemplate(RecordedAction action, IReadOnlyDictionary<string, object?>? parameters)
    {
        return new RecordedAction
        {
            Kind = action.Kind,
            Role = action.Role,
            Name = action.Name,
            TestId = action.TestId,
            Key = action.Key,
            Url = action.Url is null ? null : CacheKeys.Detemplate(action.Url, parameters),
            Value = action.Value is null ? null : CacheKeys.Detemplate(action.Value, parameters),
        };
    }

    private static string Interpolate(string instruction, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null)
        {
            return instruction;
        }

        var text = instruction;
        foreach (var pair in parameters)
        {
            text = text.Replace("{" + pair.Key + "}", CacheKeys.Display(pair.Value), StringComparison.Ordinal);
        }

        return text;
    }

    private static string Label(SemanticNode node)
    {
        var role = node.Role ?? "node";
        return node.Name is null ? role : role + " \"" + node.Name + "\"";
    }

    private static string ShapeOf(Type type)
    {
        if (type == typeof(string) || type.IsPrimitive)
        {
            return "{ \"value\": " + type.Name + " }";
        }

        var properties = type.GetProperties();
        if (properties.Length == 0)
        {
            return type.Name;
        }

        return "{ " + string.Join(", ", properties.Select(property => "\"" + property.Name + "\": " + property.PropertyType.Name)) + " }";
    }

    private static bool TryProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private CancellationTokenSource Link(CancellationToken cancellationToken, TimeSpan timeout)
    {
        var caller = cancellationToken == default ? _scope.Token() : cancellationToken;
        var linked = CancellationTokenSource.CreateLinkedTokenSource(caller);
        linked.CancelAfter(timeout);
        return linked;
    }

    private RecordedAction ResolveSecret(RecordedAction action)
    {
        if (action.Value is null || !action.Value.StartsWith("<secret:", StringComparison.Ordinal) || !action.Value.EndsWith('>'))
        {
            return action;
        }

        var name = action.Value["<secret:".Length..^1];
        var secret = _scope.Secrets.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (secret is null)
        {
            throw new AgentException("AUTH_CREDENTIAL_UNAVAILABLE", "Replay needs secret '" + name + "', and this step did not declare it.");
        }

        return new RecordedAction
        {
            Kind = action.Kind,
            Role = action.Role,
            Name = action.Name,
            TestId = action.TestId,
            Key = action.Key,
            Url = action.Url,
            Value = secret.Value,
        };
    }
}

/// <summary>Values passed into one <see cref="Agent.ActAsync"/> goal. Put fresh data in <see cref="Values.Unique"/>.</summary>
public sealed class ActOptions
{
    public IReadOnlyDictionary<string, object?>? Params { get; init; }

    public TimeSpan? Timeout { get; init; }

    public int? MaxModelCalls { get; init; }
}

public sealed class AssertOptions
{
    public TimeSpan? Timeout { get; init; }
}

public sealed class WaitForOptions
{
    public TimeSpan? Timeout { get; init; }

    public TimeSpan? Interval { get; init; }
}

public sealed class ActResult
{
    public required string Summary { get; init; }

    /// <summary>
    /// Null when caching is off. <c>self-finalized</c> finished with no model call,
    /// <c>agent-concluded</c> replayed then handed off, <c>missed</c> ran live from the start.
    /// </summary>
    public CacheInfo? Cache { get; init; }
}

public sealed class CacheInfo
{
    public required string Mode { get; init; }

    public string? Reason { get; init; }
}

internal sealed class AttemptScope
{
    public required IEngineSession Session { get; init; }

    public required IAgentModel? Model { get; init; }

    public required IStepCache? Cache { get; init; }

    public required bool CacheEnabled { get; init; }

    public required string TestTitle { get; init; }

    public required string EnginePlatform { get; init; }

    public required string EngineVersion { get; init; }

    public int Attempt { get; init; } = 1;

    public TimeSpan ActionTimeout { get; init; } = TimeSpan.FromSeconds(5);

    public TimeSpan StepTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public int MaxModelCalls { get; init; } = 12;

    public Func<CancellationToken> Token { get; init; } = static () => CancellationToken.None;

    public List<Secret> Secrets { get; } = [];

    public List<string> Completed { get; } = [];

    public List<PendingAct> Acts { get; } = [];

    public int ModelCalls { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int Replayed { get; set; }

    public int HandedOff { get; set; }

    public int Missed { get; set; }

    public void Remember(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null)
        {
            return;
        }

        foreach (var value in parameters.Values)
        {
            if (value is Secret secret && !Secrets.Any(item => string.Equals(item.Name, secret.Name, StringComparison.Ordinal) && string.Equals(item.Value, secret.Value, StringComparison.Ordinal)))
            {
                Secrets.Add(secret);
            }
        }
    }

    public void MarkVerified()
    {
        foreach (var act in Acts)
        {
            if (act.Completed)
            {
                act.Verified = true;
            }
        }
    }
}

internal sealed class PendingAct
{
    public required string Key { get; init; }

    public bool Completed { get; set; }

    public bool Verified { get; set; }

    public bool ParamCollision { get; set; }

    public CacheEntry? Entry { get; set; }
}

internal readonly record struct Verdict(string Status, string? Summary, string? Code);

internal readonly record struct ReplayAttempt(bool Completed, bool Handoff, CacheInfo? Info)
{
    public static ReplayAttempt Done() => new(true, false, new CacheInfo { Mode = "self-finalized" });

    public static ReplayAttempt Miss(string reason) => new(false, false, new CacheInfo { Mode = "missed", Reason = reason });

    public static ReplayAttempt Hand(string reason) => new(false, true, new CacheInfo { Mode = "agent-concluded", Reason = reason });
}

internal sealed class ToolOutcome
{
    public required string Content { get; init; }

    public bool Succeeded { get; init; }

    public bool Done { get; init; }

    public string? Status { get; init; }

    public string? Summary { get; init; }

    public string? Code { get; init; }

    public static ToolOutcome Ok(string content) => new() { Content = content, Succeeded = true };

    public static ToolOutcome Fail(string content) => new() { Content = "failed: " + content, Succeeded = false };

    public static ToolOutcome Finish(string status, string? summary, string? code) => new()
    {
        Content = "done " + status,
        Succeeded = true,
        Done = true,
        Status = status,
        Summary = summary,
        Code = code,
    };
}
