// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
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
        "blocked means credentials, the environment, or test setup prevented a verdict, and requires a code. " +
        "Never type a secret value. Call fill_secret with the secret name. " +
        "A secret looks like <secret:name>. " +
        "If what you need is not on the screen, scroll to it.";

    // A scroll to a text gives up after this many pages, or once the screen stops moving.
    private const int MaxScrollUntilScreens = 800;
    private const int ScrollUntilStillPages = 3;
    private const int ScrollUntilWrongListPages = 2;

    private const string JudgeSystem =
        "You judge one statement against the current screen. Call done. " +
        "status passed when the screen shows the statement is true. " +
        "status failed with code ASSERTION_FAILED when the statement is false. " +
        "status failed with code ASSERTION_INCONCLUSIVE when the screen does not show enough to decide. " +
        "You do not see earlier steps. Do not call any tool except done.";

    // assert and extract make one model call and one repair round.
    private const int JudgmentModelCalls = 2;

    // The most bytes an act instruction may hold.
    private const int MaxInstructionBytes = 8_192;

    // How often waitFor looks for a changed screen once its interval has passed.
    private static readonly TimeSpan WaitForTick = TimeSpan.FromMilliseconds(250);

    private static readonly JsonSerializerOptions ExtractJson = CreateExtractJson();

    private static readonly HashSet<string> ActionTools =
        new(["navigate", "tap", "double_tap", "fill", "fill_secret", "press", "select", "check", "uncheck", "clear", "back", "scroll", "scroll_to"], StringComparer.Ordinal);

    private readonly AttemptScope _scope;

    internal Agent(AttemptScope scope)
    {
        _scope = scope;
    }

    public async Task<ActResult> ActAsync(string instruction, ActOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        CheckInstruction(instruction);
        var agent = _scope.Select(options?.Agent);
        var maxCalls = Budget(options?.MaxModelCalls, agent.MaxModelCalls, "MaxModelCalls");
        var budget = new ActionBudget(Budget(options?.MaxSteps, agent.MaxSteps, "MaxSteps"));
        var timeout = ResolveTimeout(options?.Timeout, _scope.StepTimeout);
        ActParams.Validate(options?.Params);
        var callsBefore = _scope.ModelCalls;
        using var linked = Link(cancellationToken, timeout);
        var token = linked.Token;
        _scope.Remember(options?.Params);
        var signature = CacheKeys.Create(_scope.EnginePlatform, _scope.TestTitle, instruction, options?.Params);
        var key = CacheKeys.ForCall(signature, _scope.NextCallIndex(signature));
        var pending = new PendingAct { Key = key, ParamCollision = CacheKeys.Collides(options?.Params) };
        if (_scope.CacheEnabled)
        {
            _scope.Acts.Add(pending);
        }

        var start = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
        var actions = new List<RecordedAction>();
        CacheInfo? info = null;
        var handoff = false;
        var actionsAtEndMismatch = -1;
        if (_scope.CacheEnabled && _scope.Attempt == 1 && _scope.Cache is not null)
        {
            var replay = await TryReplayAsync(pending, start, actions, budget, options?.Params, token).ConfigureAwait(false);
            info = replay.Info;
            handoff = replay.Handoff;
            if (_scope.CacheStrict && !replay.Completed && info?.Reason is not null and not "no-entry")
            {
                throw new AgentException(
                    "REPLAY_STALE",
                    "The recording for '" + instruction + "' no longer matches (" + info.Reason + "). Strict cache mode does not run it live; re-record it without cache.strict.");
            }

            if (replay.Completed)
            {
                pending.Completed = true;
                pending.ReplayedWhole = true;
                pending.Entry = BuildEntry(instruction, start, await _scope.Session.ObserveAsync(token).ConfigureAwait(false), actions, options?.Params);
                _scope.Completed.Add("Replayed: " + instruction);
                return new ActResult { Summary = "Replayed recorded actions.", Cache = info, ModelCalls = 0, Actions = budget.Used };
            }

            if (handoff && string.Equals(info?.Reason, "end-mismatch", StringComparison.Ordinal))
            {
                actionsAtEndMismatch = actions.Count;
            }
        }
        else if (_scope.CacheEnabled && _scope.Cache is not null)
        {
            // A retry records like any attempt but never replays.
            _scope.Missed++;
            info = ReplayAttempt.Miss("retry").Info;
        }

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

            var response = await CallModelAsync(agent.Model, ActSystemFor(agent), messages, AgentTools.ActFor(_scope.EngineCapabilities), agent, token).ConfigureAwait(false);
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
                var outcome = await ExecuteAsync(toolCall, options?.Params, actions, budget, token).ConfigureAwait(false);
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
                if (budget.Exhausted && string.Equals(outcome.Status, "passed", StringComparison.Ordinal))
                {
                    // The model cannot declare success over the runtime's own accounting.
                    throw budget.Stop(summary);
                }

                if (string.Equals(outcome.Status, "passed", StringComparison.Ordinal))
                {
                    if (actionsAtEndMismatch >= 0 && actions.Count > actionsAtEndMismatch)
                    {
                        // The recorded actions ran but did not reach the recorded end, so they are proven
                        // not to produce it. Evict the entry and let a clean run record the flow again.
                        if (_scope.CacheWrite)
                        {
                            _scope.Cache!.Delete(key);
                        }

                        _scope.Completed.Add(summary.Length == 0 ? instruction : summary);
                        return new ActResult { Summary = summary, Cache = info, ModelCalls = _scope.ModelCalls - callsBefore, Actions = budget.Used };
                    }

                    pending.Completed = true;
                    var end = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
                    pending.Entry = BuildEntry(instruction, start, end, actions, options?.Params);
                    _scope.Completed.Add(summary.Length == 0 ? instruction : summary);
                    return new ActResult { Summary = summary, Cache = info, ModelCalls = _scope.ModelCalls - callsBefore, Actions = budget.Used };
                }

                if (string.Equals(outcome.Status, "blocked", StringComparison.Ordinal))
                {
                    throw new AgentException(code!, summary.Length == 0 ? "The step is blocked." : summary, blocked: true);
                }

                // A failure the model gave no code for inherits the exhausted budget, so the report names it.
                if (code is null && budget.Exhausted)
                {
                    throw budget.Stop(summary);
                }

                throw new AgentException(code ?? "ACTION_FAILED", summary.Length == 0 ? "The step failed." : summary);
            }

            if (failures >= 3 && failures < 5)
            {
                messages.Add(new ModelMessage { Role = "user", Content = "The last actions failed. Change approach, then call done if you cannot." });
            }
        }

        if (budget.Exhausted)
        {
            throw budget.Stop(null);
        }

        // Asking for one more call than the budget allows is a hard stop, as upstream: blocked, not a verdict.
        throw new AgentException("STEP_BUDGET_EXHAUSTED", "agent.act exhausted its model-call budget of " + maxCalls.ToString(CultureInfo.InvariantCulture), blocked: true);
    }

    public Task AssertAsync(string statement, AssertOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        var agent = _scope.Select(options?.Agent);
        return JudgeAsync(statement, ResolveTimeout(options?.Timeout, agent.JudgmentTimeout), agent, cancellationToken);
    }

    public async Task WaitForAsync(string statement, WaitForOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(statement);
        var agent = _scope.Select(options?.Agent);
        var interval = Interval(options?.Interval);
        var timeout = ResolveTimeout(options?.Timeout, agent.JudgmentTimeout);
        var maxCalls = Budget(options?.MaxModelCalls, agent.MaxModelCalls, "MaxModelCalls");
        using var linked = Link(cancellationToken, timeout);
        var token = linked.Token;
        var deadline = DateTime.UtcNow + timeout;
        var callsBefore = _scope.ModelCalls;
        var last = "the statement did not hold";
        try
        {
            var snapshot = SnapshotText.Render(await _scope.Session.ObserveAsync(token).ConfigureAwait(false), _scope.Secrets);
            while (true)
            {
                var verdict = await JudgeOnceAsync(statement, snapshot, agent, maxCalls - (_scope.ModelCalls - callsBefore), token).ConfigureAwait(false);
                if (string.Equals(verdict.Status, "passed", StringComparison.Ordinal))
                {
                    _scope.MarkVerified();
                    return;
                }

                last = string.IsNullOrWhiteSpace(verdict.Summary) ? last : verdict.Summary;

                // The next judgment waits for the interval and for a screen that changed since the last one.
                var judged = snapshot;
                var judgedAt = DateTime.UtcNow;
                while (true)
                {
                    if (DateTime.UtcNow >= deadline)
                    {
                        throw new AgentException("STEP_TIMEOUT", "waitFor timed out; last judgment: " + last);
                    }

                    if (_scope.ModelCalls - callsBefore >= maxCalls)
                    {
                        throw new AgentException("STEP_BUDGET_EXHAUSTED", "waitFor exhausted its model-call budget; last judgment: " + last);
                    }

                    var untilInterval = interval - (DateTime.UtcNow - judgedAt);
                    var wait = untilInterval > TimeSpan.Zero ? untilInterval : (interval < WaitForTick ? interval : WaitForTick);
                    var remaining = deadline - DateTime.UtcNow;
                    if (remaining > TimeSpan.Zero)
                    {
                        await Task.Delay(wait < remaining ? wait : remaining, token).ConfigureAwait(false);
                    }

                    snapshot = SnapshotText.Render(await _scope.Session.ObserveAsync(token).ConfigureAwait(false), _scope.Secrets);
                    if (DateTime.UtcNow - judgedAt >= interval && !string.Equals(snapshot, judged, StringComparison.Ordinal))
                    {
                        break;
                    }
                }
            }
        }
        catch (OperationCanceledException ex) when (linked.IsCancellationRequested && !cancellationToken.IsCancellationRequested && !_scope.Token().IsCancellationRequested)
        {
            throw new AgentException("STEP_TIMEOUT", "waitFor timed out; last judgment: " + last, ex);
        }
    }

    /// <summary>
    /// Reads data of type <typeparamref name="T"/> from the current screen. <typeparamref name="T"/> is the
    /// schema: its JSON schema is sent to the judge, and the answer must deserialize into it with required
    /// members and nullable annotations respected. An answer that does not gets one repair round, then
    /// <c>MODEL_OUTPUT_INVALID</c>.
    /// </summary>
    public async Task<T> ExtractAsync<T>(string instruction, ExtractOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instruction);
        var agent = _scope.Select(options?.Agent);
        using var linked = Link(cancellationToken, ResolveTimeout(options?.Timeout, agent.JudgmentTimeout));
        var token = linked.Token;
        var observation = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
        var snapshot = SnapshotText.Render(observation, _scope.Secrets);
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(ExtractJson, typeof(T));
        var tools = AgentTools.ExtractFor(schema);
        var messages = new List<ModelMessage>
        {
            new()
            {
                Role = "user",
                Content = SnapshotText.Redact("Instruction: " + instruction + "\n\nReturn data matching this JSON schema: " + schema.ToJsonString() + "\n\n", _scope.Secrets) + snapshot,
            },
        };
        for (var call = 1; ; call++)
        {
            var response = await CallModelAsync(agent.Judge, JudgeSystemFor(agent), messages, tools, agent, token).ConfigureAwait(false);
            var extract = response.ToolCalls.FirstOrDefault(item => string.Equals(item.Name, "extract", StringComparison.Ordinal));
            string issue;
            string? previous = null;
            IReadOnlyList<string> fields = [];
            if (extract is null)
            {
                var done = response.ToolCalls.FirstOrDefault(item => string.Equals(item.Name, "done", StringComparison.Ordinal));
                if (done is not null)
                {
                    throw new AgentException("ASSERTION_INCONCLUSIVE", "nothing to extract: " + (Args.String(done.Arguments, "summary") ?? "the screen does not show the requested data"));
                }

                issue = "call extract with the data, or done when the screen does not show it";
                previous = response.Content;
            }
            else if (!extract.Arguments.TryGetProperty("data", out var data) && !TryProperty(extract.Arguments, "data", out data))
            {
                issue = "extract takes the data in a data field";
                previous = JsonSerializer.Serialize(extract.Arguments);
            }
            else
            {
                previous = JsonSerializer.Serialize(data);
                try
                {
                    var value = data.Deserialize<T>(ExtractJson);
                    if (value is not null)
                    {
                        return value;
                    }

                    issue = "the data is null";
                }
                catch (JsonException ex)
                {
                    issue = ex.Message;
                    fields = TopLevelField(ex.Path);
                }

                if (call >= JudgmentModelCalls)
                {
                    throw new AgentException("MODEL_OUTPUT_INVALID", "extracted data failed schema validation: " + issue);
                }
            }

            if (call >= JudgmentModelCalls)
            {
                throw new AgentException("MODEL_OUTPUT_INVALID", "extract did not return data: " + issue);
            }

            AddRejected(messages, response);
            messages.Add(new ModelMessage
            {
                Role = "user",
                Content = SnapshotText.Redact(
                    Repair(
                        issue,
                        previous,
                        fields,
                        "Correct a misread or a wrong shape. When the screen cannot satisfy the errors, call done with status failed and code ASSERTION_INCONCLUSIVE and say in summary what is missing, rather than change values."),
                    _scope.Secrets),
            });
        }
    }

    private async Task JudgeAsync(string statement, TimeSpan timeout, ResolvedAgent agent, CancellationToken cancellationToken)
    {
        using var linked = Link(cancellationToken, timeout);
        var token = linked.Token;
        var observation = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
        var verdict = await JudgeOnceAsync(statement, SnapshotText.Render(observation, _scope.Secrets), agent, JudgmentModelCalls, token).ConfigureAwait(false);
        if (string.Equals(verdict.Status, "passed", StringComparison.Ordinal))
        {
            _scope.MarkVerified();
            return;
        }

        throw new AgentException(verdict.Code!, verdict.Summary ?? "The statement did not hold.");
    }

    // One judgment, with a repair round when the judge did not call done and the budget has a call left.
    private async Task<Verdict> JudgeOnceAsync(string statement, string snapshot, ResolvedAgent agent, int callsLeft, CancellationToken token)
    {
        var messages = new List<ModelMessage>
        {
            new() { Role = "user", Content = SnapshotText.Redact("Statement: " + statement + "\n\n", _scope.Secrets) + snapshot },
        };
        var system = JudgeSystemFor(agent);
        var response = await CallModelAsync(agent.Judge, system, messages, AgentTools.Judge, agent, token).ConfigureAwait(false);
        var verdict = ReadVerdict(response);
        if (verdict is not null)
        {
            return verdict.Value;
        }

        if (callsLeft < 2)
        {
            throw new AgentException("MODEL_OUTPUT_INVALID", "The judge did not call done.");
        }

        AddRejected(messages, response);
        messages.Add(new ModelMessage { Role = "user", Content = "Call done with status passed or failed." });
        response = await CallModelAsync(agent.Judge, system, messages, AgentTools.Judge, agent, token).ConfigureAwait(false);
        verdict = ReadVerdict(response);
        if (verdict is null)
        {
            throw new AgentException("MODEL_OUTPUT_INVALID", "The judge did not call done.");
        }

        return verdict.Value;
    }

    // A rejected answer stays in the transcript, with a reply to each of its tool calls.
    private static void AddRejected(List<ModelMessage> messages, ModelResponse response)
    {
        messages.Add(new ModelMessage { Role = "assistant", Content = response.Content, ToolCalls = response.ToolCalls });
        foreach (var call in response.ToolCalls)
        {
            messages.Add(new ModelMessage { Role = "tool", ToolCallId = call.Id, Name = call.Name, Content = "rejected" });
        }
    }

    private static string Repair(string issue, string? previous, IReadOnlyList<string> fields, string outlet)
    {
        var builder = new StringBuilder();
        builder.Append("<previous-attempt-rejected>\n");
        builder.Append("Your previous response was rejected. Do not repeat it.\n");
        if (previous is not null)
        {
            builder.Append("previous response: ").Append(previous.Length > 2000 ? previous[..2000] : previous).Append('\n');
        }

        builder.Append("validation errors: ").Append(issue).Append('\n');
        builder.Append(fields.Count == 0
            ? "Field paths in the errors describe the exact output shape required."
            : "The value must be a JSON object whose top-level fields include: " + string.Join(", ", fields) + ".");
        builder.Append('\n');
        builder.Append("Return a corrected response that satisfies every error above.\n");
        builder.Append(outlet).Append('\n');
        builder.Append("</previous-attempt-rejected>");
        return builder.ToString();
    }

    // The first member of a JSON path such as $.price or $['unit price'][0].
    private static IReadOnlyList<string> TopLevelField(string? path)
    {
        if (string.IsNullOrEmpty(path) || !path.StartsWith('$') || path.Length < 2)
        {
            return [];
        }

        if (path[1] == '.')
        {
            var end = path.IndexOfAny(['.', '['], 2);
            var name = end < 0 ? path[2..] : path[2..end];
            return name.Length == 0 ? [] : [name];
        }

        if (path.Length > 3 && path[1] == '[' && path[2] == '\'')
        {
            var end = path.IndexOf("']", 3, StringComparison.Ordinal);
            return end < 0 ? [] : [path[3..end]];
        }

        return [];
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

        // A judge answers holds, fails, or inconclusive. A judge that cannot decide is inconclusive.
        string? code = null;
        if (!string.Equals(status, "passed", StringComparison.Ordinal))
        {
            code = status == "blocked" || Args.String(done.Arguments, "code") == "ASSERTION_INCONCLUSIVE" ? "ASSERTION_INCONCLUSIVE" : "ASSERTION_FAILED";
        }

        return new Verdict(status, Args.String(done.Arguments, "summary"), code);
    }

    private async Task<ReplayAttempt> TryReplayAsync(
        PendingAct pending,
        Observation start,
        List<RecordedAction> actions,
        ActionBudget budget,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken token)
    {
        var lookup = _scope.Cache!.Read(pending.Key);
        if (lookup.Entry is null)
        {
            _scope.Missed++;
            return ReplayAttempt.Miss(lookup.Reason ?? "no-entry");
        }

        var entry = lookup.Entry;
        if (entry.Actions.Count == 0)
        {
            _scope.Missed++;
            return ReplayAttempt.Miss("invalid-entry");
        }

        // A recording that opens with navigate sets up its own start screen.
        var opensWithNavigate = string.Equals(entry.Actions[0].Kind, "navigate", StringComparison.Ordinal);
        if (!opensWithNavigate && !string.Equals(entry.Route, start.Route, StringComparison.Ordinal))
        {
            _scope.Missed++;
            return ReplayAttempt.Miss("wrong-context");
        }

        pending.ConsumedReplay = true;

        var started = false;
        ReplayAttempt Lost(string? reason)
        {
            if (!started)
            {
                _scope.Missed++;
                actions.Clear();
                return ReplayAttempt.Miss(reason ?? "target-not-found");
            }

            _scope.HandedOff++;
            return ReplayAttempt.Hand(reason ?? "target-not-found");
        }

        foreach (var action in entry.Actions)
        {
            if (string.Equals(action.Kind, "navigate", StringComparison.Ordinal))
            {
                if (!budget.TryReserve())
                {
                    _scope.HandedOff++;
                    return ReplayAttempt.Hand("action-budget");
                }

                await _scope.Session.OpenAsync(action.Url ?? "/", token).ConfigureAwait(false);
                actions.Add(action);
                started = true;
                continue;
            }

            // A replayed action draws on the step's action budget like a live one. The first always fits.
            // An action with a target reserves its slot once the target is found, so a lost target is a miss.
            if ((action.Kind is "back" or "scroll" or "scrollUntil" || (action.Kind == "press" && !HasTarget(action))) && !budget.TryReserve())
            {
                _scope.HandedOff++;
                return ReplayAttempt.Hand("action-budget");
            }

            try
            {
                // back, a viewport scroll, and a key press on the focused control target nothing, so they replay as given.
                // A scroll on a list re-finds the list before each repeat.
                switch (action.Kind)
                {
                    case "back":
                        await _scope.Session.BackAsync(token).ConfigureAwait(false);
                        break;
                    case "press" when !HasTarget(action):
                        await _scope.Session.PressAsync(action.Key ?? "Enter", token).ConfigureAwait(false);
                        break;
                    case "scroll" or "scrollUntil" when !TryDirection(action.Direction, out _):
                        return Lost("invalid-entry");
                    case "scroll" when !HasTarget(action):
                        for (var repeat = 0; repeat < (action.Times ?? 1); repeat++)
                        {
                            await _scope.Session.SwipeAsync(Direction(action.Direction), token).ConfigureAwait(false);
                        }

                        break;
                    case "scroll":
                        for (var repeat = 0; repeat < (action.Times ?? 1); repeat++)
                        {
                            var list = await WaitForTargetAsync(action, token).ConfigureAwait(false);
                            if (list.Node is null)
                            {
                                return Lost(list.Reason);
                            }

                            await _scope.Session.PerformAsync(list.Node, new LocatorAction.Swipe(Direction(action.Direction)), token).ConfigureAwait(false);
                            started = true;
                        }

                        break;
                    case "scrollUntil":
                        if (HasTarget(action))
                        {
                            var list = await WaitForTargetAsync(action, token).ConfigureAwait(false);
                            if (list.Node is null)
                            {
                                return Lost(list.Reason);
                            }
                        }

                        var text = CacheKeys.Detemplate(action.Text ?? "", parameters);
                        await ScrollUntilAsync(text, Direction(action.Direction), HasTarget(action) ? action : null, token).ConfigureAwait(false);
                        break;
                    default:
                        var found = await WaitForTargetAsync(action, token).ConfigureAwait(false);
                        if (found.Node is null)
                        {
                            return Lost(found.Reason);
                        }

                        if (!budget.TryReserve())
                        {
                            _scope.HandedOff++;
                            return ReplayAttempt.Hand("action-budget");
                        }

                        var performed = ResolveSecret(Detemplate(action, parameters));
                        await PerformRecordedAsync(found.Node, performed, token).ConfigureAwait(false);
                        break;
                }

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

    // The last action may start a navigation or a slow render, so the end route and anchors get the replay timeout to show up.
    private async Task<bool> WaitForEndAsync(CacheEntry entry, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + _scope.ReplayTimeout;
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
        var deadline = DateTime.UtcNow + _scope.ReplayTimeout;
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
            "doubleTap" => new LocatorAction.DoubleTap(),
            "fill" => new LocatorAction.Fill(action.Value ?? "", false),
            "press" => new LocatorAction.Press(action.Key ?? "Enter"),
            "select" => new LocatorAction.Select(action.Value ?? ""),
            "check" => new LocatorAction.Check(),
            "uncheck" => new LocatorAction.Uncheck(),
            "clear" => new LocatorAction.Clear(),
            "scrollTo" => new LocatorAction.ScrollIntoView(),
            _ => throw new AgentException("AUTOMATION_UNSUPPORTED", "Cannot replay " + action.Kind + "."),
        };
        await _scope.Session.PerformAsync(node, locatorAction, token).ConfigureAwait(false);
    }

    private async Task<ToolOutcome> ExecuteAsync(
        ModelToolCall call,
        IReadOnlyDictionary<string, object?>? parameters,
        List<RecordedAction> actions,
        ActionBudget budget,
        CancellationToken token)
    {
        if (string.Equals(call.Name, "done", StringComparison.Ordinal))
        {
            var status = Args.String(call.Arguments, "status");
            if (status is not ("passed" or "failed" or "blocked"))
            {
                return ToolOutcome.Fail("done status must be passed, failed, or blocked.");
            }

            // The model picks only from a closed set of codes. The runtime assigns the rest.
            var code = Args.String(call.Arguments, "code");
            if (string.Equals(status, "passed", StringComparison.Ordinal) || !AgentTools.ModelErrorCodes.Contains(code))
            {
                code = null;
            }

            if (string.Equals(status, "blocked", StringComparison.Ordinal) && !AgentTools.BlockableCodes.Contains(code))
            {
                return ToolOutcome.Fail(
                    "a blocked verdict requires a code naming what blocked you (one of " + string.Join(", ", AgentTools.BlockableCodes) + "). " +
                    "If the application itself misbehaved, use status failed instead.");
            }

            return ToolOutcome.Finish(status, Args.String(call.Arguments, "summary"), code);
        }

        // observe looks and records nothing, so it takes no action slot.
        if (string.Equals(call.Name, "observe", StringComparison.Ordinal))
        {
            return ToolOutcome.Ok(await DescribeAsync("observed", token).ConfigureAwait(false));
        }

        if (!ActionTools.Contains(call.Name))
        {
            return ToolOutcome.Fail("Unknown tool " + call.Name + ".");
        }

        // Every action claims a budget slot before it runs. A failed action was still an attempt.
        if (!budget.TryReserve())
        {
            return ToolOutcome.Fail(budget.Message + ". Take no more actions. Call done with a verdict.");
        }

        switch (call.Name)
        {
            case "back":
                return await BackAsync(actions, token).ConfigureAwait(false);
            case "scroll":
                return await ScrollAsync(call.Arguments, actions, token).ConfigureAwait(false);
            case "scroll_to":
                return await ScrollToAsync(call.Arguments, parameters, actions, token).ConfigureAwait(false);
        }

        if (string.Equals(call.Name, "navigate", StringComparison.Ordinal))
        {
            var url = Args.String(call.Arguments, "url") ?? "/";
            await _scope.Session.OpenAsync(url, token).ConfigureAwait(false);
            actions.Add(new RecordedAction { Kind = "navigate", Url = url });
            return ToolOutcome.Ok(await DescribeAsync("navigated to " + Routes.PathOf(url), token).ConfigureAwait(false));
        }

        if (string.Equals(call.Name, "press", StringComparison.Ordinal) && !HasTarget(call.Arguments))
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
                case "double_tap":
                    await _scope.Session.PerformAsync(node, new LocatorAction.DoubleTap(), token).ConfigureAwait(false);
                    actions.Add(Record(node, "doubleTap"));
                    return ToolOutcome.Ok(await DescribeAsync("double-tapped " + Label(node), token).ConfigureAwait(false));
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

    private async Task<ToolOutcome> BackAsync(List<RecordedAction> actions, CancellationToken token)
    {
        try
        {
            await _scope.Session.BackAsync(token).ConfigureAwait(false);
        }
        catch (E2EException ex)
        {
            return ToolOutcome.Fail(ex.Message);
        }

        actions.Add(new RecordedAction { Kind = "back" });
        return ToolOutcome.Ok(await DescribeAsync("navigated back", token).ConfigureAwait(false));
    }

    private async Task<ToolOutcome> ScrollAsync(JsonElement arguments, List<RecordedAction> actions, CancellationToken token)
    {
        if (!TryDirection(Args.String(arguments, "direction"), out var direction))
        {
            return ToolOutcome.Fail("scroll direction must be up, down, left, or right.");
        }

        var times = Args.Int(arguments, "times") ?? 1;
        if (times is < 1 or > AgentTools.MaxScrollTimes)
        {
            return ToolOutcome.Fail("scroll times must be 1 to " + AgentTools.MaxScrollTimes.ToString(CultureInfo.InvariantCulture) + ".");
        }

        try
        {
            var list = HasTarget(arguments) ? await ResolveAsync(arguments, token).ConfigureAwait(false) : null;
            for (var repeat = 0; repeat < times; repeat++)
            {
                if (list is null)
                {
                    await _scope.Session.SwipeAsync(direction, token).ConfigureAwait(false);
                }
                else
                {
                    await _scope.Session.PerformAsync(list, new LocatorAction.Swipe(direction), token).ConfigureAwait(false);
                }

                RecordScroll(actions, direction, list);
            }

            var done = "scrolled" + (list is null ? "" : " " + Label(list)) + " " + DirectionName(direction);
            if (times > 1)
            {
                done += " " + times.ToString(CultureInfo.InvariantCulture) + " screens";
            }

            return ToolOutcome.Ok(await DescribeAsync(done, token).ConfigureAwait(false));
        }
        catch (E2EException ex)
        {
            return ToolOutcome.Fail(ex.Message);
        }
    }

    private async Task<ToolOutcome> ScrollToAsync(
        JsonElement arguments,
        IReadOnlyDictionary<string, object?>? parameters,
        List<RecordedAction> actions,
        CancellationToken token)
    {
        var text = Args.String(arguments, "text");
        try
        {
            if (text is not null)
            {
                if (string.IsNullOrWhiteSpace(text) || text.Length > AgentTools.MaxScrollToText)
                {
                    return ToolOutcome.Fail("scroll_to text must be the text to reach, up to " + AgentTools.MaxScrollToText.ToString(CultureInfo.InvariantCulture) + " characters.");
                }

                var way = Args.String(arguments, "direction");
                var direction = ScrollDirection.Down;
                if (way is not null && !TryDirection(way, out direction))
                {
                    return ToolOutcome.Fail("scroll_to direction must be up, down, left, or right.");
                }

                var list = HasTarget(arguments) ? Record(await ResolveAsync(arguments, token).ConfigureAwait(false), "scrollUntil") : null;
                await ScrollUntilAsync(text, direction, list, token).ConfigureAwait(false);
                actions.Add(new RecordedAction
                {
                    Kind = "scrollUntil",
                    Role = list?.Role,
                    Name = list?.Name,
                    TestId = list?.TestId,
                    Direction = DirectionName(direction),
                    Text = CacheKeys.Template(text, parameters),
                });
                return ToolOutcome.Ok(await DescribeAsync("scrolled " + DirectionName(direction) + " until \"" + text + "\" was in view", token).ConfigureAwait(false));
            }

            if (!HasTarget(arguments))
            {
                return ToolOutcome.Fail("scroll_to takes a target, a text to reach, or both.");
            }

            var node = await ResolveAsync(arguments, token).ConfigureAwait(false);
            await _scope.Session.PerformAsync(node, new LocatorAction.ScrollIntoView(), token).ConfigureAwait(false);
            actions.Add(Record(node, "scrollTo"));
            return ToolOutcome.Ok(await DescribeAsync("scrolled " + Label(node) + " into view", token).ConfigureAwait(false));
        }
        catch (E2EException ex)
        {
            return ToolOutcome.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Pages a list, or the viewport, until a node reading <paramref name="text"/> is on the
    /// screen, then brings it into view. A screen that stops moving ends the paging, and a
    /// list that does not move hands the paging to the viewport. The list is re-found before
    /// every page.
    /// </summary>
    private async Task ScrollUntilAsync(string text, ScrollDirection direction, RecordedAction? list, CancellationToken token)
    {
        string? previous = null;
        var still = 0;
        for (var screens = 0; ; screens++)
        {
            var observation = await _scope.Session.ObserveAsync(token).ConfigureAwait(false);
            var within = list is null ? null : Single(Find(observation, list.Role, list.Name, list.TestId, null));
            var found = Reading(within is null ? observation.Roots : within.Children, text);
            if (found is not null)
            {
                await _scope.Session.PerformAsync(found, new LocatorAction.ScrollIntoView(), token).ConfigureAwait(false);
                return;
            }

            if (screens >= MaxScrollUntilScreens)
            {
                throw new TestException("LOCATOR_NOT_FOUND", "Nothing reading \"" + text + "\" came into view within " + MaxScrollUntilScreens.ToString(CultureInfo.InvariantCulture) + " screens.");
            }

            // The tree can stay the same while the page moves under it (every node
            // already fits the budget), so the scroll position counts too.
            var shape = SnapshotText.Render(observation, []) + "\n" + observation.ScrollPosition;
            still = string.Equals(shape, previous, StringComparison.Ordinal) ? still + 1 : 0;
            if (still >= ScrollUntilStillPages)
            {
                throw new TestException(
                    "LOCATOR_NOT_FOUND",
                    "Nothing reading \"" + text + "\" came into view before the screen stopped moving " + DirectionName(direction) + ", after " + screens.ToString(CultureInfo.InvariantCulture) + " screens.");
            }

            if (still >= ScrollUntilWrongListPages && within is not null)
            {
                within = null;
                still = 0;
            }

            previous = shape;
            if (within is null)
            {
                // A list that is gone, or that does not move, gives the paging to the viewport for good.
                list = null;
                await _scope.Session.SwipeAsync(direction, token).ConfigureAwait(false);
            }
            else
            {
                await _scope.Session.PerformAsync(within, new LocatorAction.Swipe(direction), token).ConfigureAwait(false);
            }
        }
    }

    // The innermost visible node whose name or text contains the text.
    private static SemanticNode? Reading(IReadOnlyList<SemanticNode> nodes, string text)
    {
        foreach (var node in nodes)
        {
            if (node.States.Hidden)
            {
                continue;
            }

            var inner = Reading(node.Children, text);
            if (inner is not null)
            {
                return inner;
            }

            if (TextRules.Matches(node.Name, text, exact: false) || TextRules.Matches(node.Text, text, exact: false))
            {
                return node;
            }
        }

        return null;
    }

    private static SemanticNode? Single(List<SemanticNode> matches)
    {
        return matches.Count == 1 ? matches[0] : null;
    }

    // Consecutive identical scrolls fold into one recorded action with a repeat count.
    private static void RecordScroll(List<RecordedAction> actions, ScrollDirection direction, SemanticNode? list)
    {
        var way = DirectionName(direction);
        if (actions.Count > 0
            && actions[^1] is { Kind: "scroll" } last
            && string.Equals(last.Direction, way, StringComparison.Ordinal)
            && string.Equals(last.Role, list?.Role, StringComparison.Ordinal)
            && string.Equals(last.Name, list?.Name, StringComparison.Ordinal)
            && string.Equals(last.TestId, list?.TestId, StringComparison.Ordinal))
        {
            last.Times = (last.Times ?? 1) + 1;
            return;
        }

        actions.Add(new RecordedAction { Kind = "scroll", Role = list?.Role, Name = list?.Name, TestId = list?.TestId, Direction = way });
    }

    private static bool HasTarget(JsonElement arguments)
    {
        return Args.String(arguments, "role") is not null
            || Args.String(arguments, "name") is not null
            || Args.String(arguments, "testId") is not null
            || Args.String(arguments, "ref") is not null;
    }

    private static bool HasTarget(RecordedAction action)
    {
        return action.Role is not null || action.Name is not null || action.TestId is not null;
    }

    private static bool TryDirection(string? value, out ScrollDirection direction)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "up":
                direction = ScrollDirection.Up;
                return true;
            case "down":
                direction = ScrollDirection.Down;
                return true;
            case "left":
                direction = ScrollDirection.Left;
                return true;
            case "right":
                direction = ScrollDirection.Right;
                return true;
            default:
                direction = ScrollDirection.Down;
                return false;
        }
    }

    private static ScrollDirection Direction(string? value)
    {
        TryDirection(value, out var direction);
        return direction;
    }

    private static string DirectionName(ScrollDirection direction)
    {
        return direction switch
        {
            ScrollDirection.Up => "up",
            ScrollDirection.Left => "left",
            ScrollDirection.Right => "right",
            _ => "down",
        };
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
                builder.Append("- ").Append(Describe(action)).Append('\n');
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

    private async Task<ModelResponse> CallModelAsync(
        IAgentModel? model,
        string system,
        List<ModelMessage> messages,
        IReadOnlyList<ModelTool> tools,
        ResolvedAgent agent,
        CancellationToken token)
    {
        if (model is null)
        {
            throw new AgentException("MODEL_UNAVAILABLE", "No model is configured. Agent steps need an IAgentModel, or a replay that finishes on its own.");
        }

        ModelResponse response;
        try
        {
            var request = new ModelRequest
            {
                System = SnapshotText.Redact(system, _scope.Secrets),
                Messages = messages,
                Tools = tools,
                ProviderOptions = agent.ProviderOptions,
            };
            response = await model.CompleteAsync(request, token).ConfigureAwait(false);
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

            // The replay needs exactly one match, so a repeated control is no anchor.
            if (Find(after, node.Role, node.Name, node.TestId, null).Count != 1)
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
            Direction = action.Direction,
            Times = action.Times,
            Text = action.Text is null ? null : CacheKeys.Detemplate(action.Text, parameters),
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

    private static string Describe(RecordedAction action)
    {
        var builder = new StringBuilder(action.Kind);
        if (action.Role is not null || action.Name is not null || action.TestId is not null)
        {
            builder.Append(' ').Append(action.Role ?? "node");
            if (action.Name is not null)
            {
                builder.Append(" \"").Append(action.Name).Append('"');
            }
            else if (action.TestId is not null)
            {
                builder.Append(" testId=").Append(action.TestId);
            }
        }

        if (action.Url is not null)
        {
            builder.Append(' ').Append(action.Url);
        }

        if (action.Text is not null)
        {
            builder.Append(" \"").Append(action.Text).Append('"');
        }

        if (action.Direction is not null)
        {
            builder.Append(' ').Append(action.Direction);
        }

        if (action.Times is > 1)
        {
            builder.Append(" x").Append(action.Times.Value.ToString(CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    private static string Label(SemanticNode node)
    {
        var role = node.Role ?? "node";
        return node.Name is null ? role : role + " \"" + node.Name + "\"";
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

    // The configured system text follows the built-in rules, and the project context follows both.
    private static string ActSystemFor(ResolvedAgent agent)
    {
        var system = ActSystem;
        if (!string.IsNullOrWhiteSpace(agent.System))
        {
            system += "\n\n" + agent.System;
        }

        if (!string.IsNullOrWhiteSpace(agent.Context))
        {
            system += "\n\nProject context:\n" + agent.Context;
        }

        return system;
    }

    // A judge never sees the act system text, so nothing in it can talk a judge into a verdict.
    private static string JudgeSystemFor(ResolvedAgent agent)
    {
        return string.IsNullOrWhiteSpace(agent.Context)
            ? JudgeSystem
            : JudgeSystem + "\n\n<project-context>\n" + agent.Context + "\n</project-context>";
    }

    private static void CheckInstruction(string instruction)
    {
        var bytes = Encoding.UTF8.GetByteCount(instruction.Normalize(NormalizationForm.FormC));
        if (bytes > MaxInstructionBytes)
        {
            throw new TestException(
                "INVALID_ARGUMENT",
                "agent.act instruction is " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes; the maximum is " + MaxInstructionBytes.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static TimeSpan ResolveTimeout(TimeSpan? requested, TimeSpan fallback)
    {
        if (requested is null)
        {
            return fallback;
        }

        if (requested <= TimeSpan.Zero)
        {
            throw new TestException("INVALID_ARGUMENT", "timeout must be a positive duration");
        }

        return requested.Value;
    }

    private static TimeSpan Interval(TimeSpan? requested)
    {
        var value = requested ?? E2EDefaults.WaitForInterval;
        if (value < TimeSpan.FromMilliseconds(100) || value > TimeSpan.FromMilliseconds(60_000))
        {
            throw new TestException(
                "INVALID_ARGUMENT",
                "interval must be from 100 through 60000 milliseconds, got " + value.TotalMilliseconds.ToString(CultureInfo.InvariantCulture));
        }

        return value;
    }

    private static JsonSerializerOptions CreateExtractJson()
    {
        var options = new JsonSerializerOptions(JsonDefaults.Options)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
            WriteIndented = false,
            TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        };
        options.MakeReadOnly();
        return options;
    }

    /// <summary>A per-call budget can only lower the configured limit, never raise it.</summary>
    private static int Budget(int? requested, int limit, string label)
    {
        if (requested is null)
        {
            return limit;
        }

        if (requested <= 0)
        {
            throw new TestException("INVALID_ARGUMENT", label + " must be a positive integer.");
        }

        if (requested > limit)
        {
            throw new TestException(
                "INVALID_ARGUMENT",
                label + " " + requested.Value.ToString(CultureInfo.InvariantCulture) + " exceeds the configured limit " + limit.ToString(CultureInfo.InvariantCulture) + ".");
        }

        return requested.Value;
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
            Direction = action.Direction,
            Times = action.Times,
            Text = action.Text,
        };
    }
}

/// <summary>Values passed into one <see cref="Agent.ActAsync"/> goal. Put fresh data in <see cref="Values.Unique"/>.</summary>
public sealed class ActOptions
{
    public IReadOnlyDictionary<string, object?>? Params { get; init; }

    public TimeSpan? Timeout { get; init; }

    /// <summary>Action budget. Defaults to the configured <c>MaxSteps</c> (25) and can only lower it.</summary>
    public int? MaxSteps { get; init; }

    /// <summary>Model-call budget. Defaults to the configured <c>MaxModelCalls</c> and can only lower it.</summary>
    public int? MaxModelCalls { get; init; }

    /// <summary>The named agent that runs the step. Defaults to <c>default</c>.</summary>
    public string? Agent { get; init; }
}

/// <summary><c>agent.assert</c>: one judgment and one repair round, within <see cref="Timeout"/>.</summary>
public sealed class AssertOptions
{
    /// <summary>Defaults to the agent's <c>JudgmentTimeout</c> (30 s).</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>The named agent whose judge decides. Defaults to <c>default</c>.</summary>
    public string? Agent { get; init; }
}

/// <summary><c>agent.waitFor</c>: a judgment at most once per <see cref="Interval"/>, and only on a changed screen, until <see cref="Timeout"/>.</summary>
public sealed class WaitForOptions
{
    /// <summary>Defaults to the agent's <c>JudgmentTimeout</c> (30 s).</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Least time between two judgments, 100 ms through 60 s. Defaults to 3 s.</summary>
    public TimeSpan? Interval { get; init; }

    /// <summary>Model-call budget. Defaults to the configured <c>MaxModelCalls</c> and can only lower it.</summary>
    public int? MaxModelCalls { get; init; }

    /// <summary>The named agent whose judge decides. Defaults to <c>default</c>.</summary>
    public string? Agent { get; init; }
}

/// <summary>
/// <c>agent.extract</c>: one extraction and one repair round. The type argument of
/// <see cref="Agent.ExtractAsync{T}"/> is the schema.
/// </summary>
public sealed class ExtractOptions
{
    /// <summary>Defaults to the agent's <c>JudgmentTimeout</c> (30 s).</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>The named agent whose judge reads the screen. Defaults to <c>default</c>.</summary>
    public string? Agent { get; init; }
}

public sealed class ActResult
{
    public required string Summary { get; init; }

    /// <summary>
    /// Null when caching is off. <c>self-finalized</c> finished with no model call,
    /// <c>agent-concluded</c> replayed then handed off, <c>missed</c> ran live from the start.
    /// </summary>
    public CacheInfo? Cache { get; init; }

    /// <summary>Model calls the step spent. 0 when a replay finished it.</summary>
    public int ModelCalls { get; init; }

    /// <summary>Actions the step performed, replayed or live, counting ones that failed.</summary>
    public int Actions { get; init; }
}

public sealed class CacheInfo
{
    public required string Mode { get; init; }

    public string? Reason { get; init; }
}

internal sealed class AttemptScope
{
    private readonly Dictionary<string, int> _callIndexes = new(StringComparer.Ordinal);

    public required IEngineSession Session { get; init; }

    /// <summary>The agents by name. <c>default</c> is always present.</summary>
    public required IReadOnlyDictionary<string, ResolvedAgent> Agents { get; init; }

    public required IStepCache? Cache { get; init; }

    public required bool CacheEnabled { get; init; }

    /// <summary>False in read-only mode: the attempt replays but does not write or delete recordings.</summary>
    public bool CacheWrite { get; init; }

    public bool CacheStrict { get; init; }

    public TimeSpan CleanupTimeout { get; init; } = E2EDefaults.CleanupTimeout;

    public required string TestTitle { get; init; }

    public required string EnginePlatform { get; init; }

    /// <summary>What the engine declared. Scroll and back tools are offered only when it can honor them.</summary>
    public EngineCapabilities EngineCapabilities { get; init; }

    public int Attempt { get; init; } = 1;

    public TimeSpan ActionTimeout { get; init; } = E2EDefaults.ActionTimeout;

    public TimeSpan ReplayTimeout { get; init; } = E2EDefaults.ReplayTimeout;

    public TimeSpan StepTimeout { get; init; } = E2EDefaults.StepTimeout;

    public Func<CancellationToken> Token { get; init; } = static () => CancellationToken.None;

    /// <summary>True once the test has failed. Verification stops there, so a later teardown check proves nothing.</summary>
    public Func<bool> TestFailed { get; init; } = static () => false;

    public List<Secret> Secrets { get; } = [];

    public List<string> Completed { get; } = [];

    public List<PendingAct> Acts { get; } = [];

    public int ModelCalls { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public int Replayed { get; set; }

    public int HandedOff { get; set; }

    public int Missed { get; set; }

    /// <summary>
    /// The zero-based repeat of this signature in the attempt. Only an identical act counts,
    /// so an optional step does not renumber the acts after it.
    /// </summary>
    public int NextCallIndex(string signature)
    {
        var index = _callIndexes.GetValueOrDefault(signature);
        _callIndexes[signature] = index + 1;
        return index;
    }

    /// <summary>The agent a call names, or <c>default</c>. An unknown name is <c>INVALID_ARGUMENT</c>.</summary>
    public ResolvedAgent Select(string? name)
    {
        if (name is not null && name.Trim().Length == 0)
        {
            throw new TestException("INVALID_ARGUMENT", "agent must be the name of a configured agent");
        }

        name ??= "default";
        if (Agents.TryGetValue(name, out var agent))
        {
            return agent;
        }

        throw new TestException("INVALID_ARGUMENT", "unknown agent \"" + name + "\"; configured: " + string.Join(", ", Agents.Keys));
    }

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
        if (TestFailed())
        {
            return;
        }

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

    /// <summary>A recording was read and replay ran at least up to its first action.</summary>
    public bool ConsumedReplay { get; set; }

    /// <summary>Replay finished the act with no model call, so the stored entry is already this flow.</summary>
    public bool ReplayedWhole { get; set; }

    public CacheEntry? Entry { get; set; }
}

/// <summary>The action slots of one act step. Replayed and live actions draw on the same budget.</summary>
internal sealed class ActionBudget(int max)
{
    public int Used { get; private set; }

    public bool Exhausted { get; private set; }

    public string Message => "agent.act exhausted its action budget of " + max.ToString(CultureInfo.InvariantCulture);

    public bool TryReserve()
    {
        if (Used >= max)
        {
            Exhausted = true;
            return false;
        }

        Used++;
        return true;
    }

    public AgentException Stop(string? summary)
    {
        return new AgentException("STEP_BUDGET_EXHAUSTED", string.IsNullOrEmpty(summary) ? Message + "." : Message + ": " + summary, blocked: true);
    }
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
