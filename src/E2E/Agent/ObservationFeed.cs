// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>
/// One step's looks at the screen. It remembers the newest observation, which an action is resolved against, and the
/// change wait the newest committed action armed, kept until the next settled look has waited for the screen to leave
/// the shape the action was resolved against. Without it, the look after a tap on a link reads the old page, stable and
/// wrong, and a replay acts on a form mid-update.
/// </summary>
internal sealed class ObservationFeed(IEngineSession session, TimeProvider time)
{
    private readonly Dictionary<string, SemanticNode> _seen = [];
    private PendingChange? _pending;

    /// <summary>How far each look of the step settled, in order.</summary>
    public List<SettleMode> Looks { get; } = [];

    /// <summary>The newest observation of the step, if any was captured.</summary>
    public Observation? Latest { get; private set; }

    /// <summary>
    /// The screen the step's first committed action was resolved against. A screen still loading when the step began
    /// can hold still long enough to pass a held-still look, so the step's delta is read from here: the page load
    /// before it is not the step's effect.
    /// </summary>
    public Observation? FirstActedOn { get; private set; }

    /// <summary>
    /// One look, settled as far as <paramref name="mode"/> asks. A raw look reads the screen as it is. A settled look
    /// consumes the pending change: it waits for the screen to leave the pre-action shape once, so later looks read the
    /// screen as it is; held still, it then proves the new shape holds for a beat.
    /// </summary>
    public async Task<Observation> ObserveAsync(SettleMode mode, CancellationToken token)
    {
        Looks.Add(mode);
        Observation observation;
        if (mode == SettleMode.Raw)
        {
            observation = await session.ObserveAsync(token).ConfigureAwait(false);
        }
        else
        {
            var changedFrom = _pending;
            _pending = null;
            observation = await AgentObservation.SettleAsync(
                session.ObserveAsync,
                AgentObservation.Shape,
                new SettleOptions<Observation>
                {
                    ChangedFrom = changedFrom,
                    Transitional = AgentObservation.IsTransitional,
                    StableWait = mode == SettleMode.HeldStill ? SettlePolicy.HeldStill : TimeSpan.Zero,
                },
                time,
                token).ConfigureAwait(false);
        }

        if (Latest is not null && !string.Equals(Latest.Route, observation.Route, StringComparison.Ordinal))
        {
            // Refs are per document, so a ref seen on another page names nothing here.
            _seen.Clear();
        }

        Latest = observation;
        foreach (var node in LocatorResolver.Walk(observation.Roots))
        {
            _seen[node.Ref] = node;
        }

        return observation;
    }

    /// <summary>The newest node an observation of the step listed under <paramref name="reference"/>, if any.</summary>
    public SemanticNode? LastSeen(string reference) => _seen.GetValueOrDefault(reference);

    /// <summary>
    /// Marks the newest observation as the one the step's next action is taken on, when no action came before. An
    /// action that looks at the screen while it acts, paging a list to a text, marks it before its first page.
    /// </summary>
    public void MarkActing()
    {
        FirstActedOn ??= Latest;
    }

    /// <summary>
    /// Arms the change wait the committed action's settle policy asks for, against the newest observation's shape, so
    /// the next settled look reads the screen after the effect rather than before. An action whose effect the tree
    /// cannot show arms nothing. The newest observation is the one the action was resolved against.
    /// </summary>
    public void ArmAfter(RecordedAction action)
    {
        MarkActing();
        if (Latest is not null && SettlePolicy.For(action).ChangeWait is { } wait)
        {
            _pending = new PendingChange(AgentObservation.Shape(Latest), time.GetUtcNow() + wait);
        }
    }

    /// <summary>
    /// Upstream's <c>relocate</c>: finds a node that went stale in one fresh look, exactly one match or null. The look is
    /// taken directly, as upstream's is, because this runs inside an action. Upstream matches with the trace recorder's
    /// descriptor rules (<c>cache/relocate.ts</c>), which also read placeholder, input purpose, and container, and
    /// forgive a changed test id. The port matches the role, name, and test id it records.
    /// </summary>
    public async Task<SemanticNode?> Relocate(SemanticNode stale, Redactor redactor, CancellationToken token)
    {
        var observation = await ObserveAsync(SettleMode.Raw, token).ConfigureAwait(false);
        var matches = FindLike(observation, stale, redactor);
        return matches.Count == 1 ? matches[0] : null;
    }

    /// <summary>
    /// The nodes that read as <paramref name="node"/> did. A node with no name and no test id has nothing to tell it
    /// from its twins, so it is never found again: a wrong match would act on the wrong control.
    /// </summary>
    public static List<SemanticNode> FindLike(Observation observation, SemanticNode node, Redactor redactor)
    {
        var name = Agent.Redact(Agent.LabelOf(node), redactor);
        var testId = Agent.Redact(node.TestId, redactor);
        return name is null && testId is null ? [] : Agent.Find(observation, node.Role, name, testId, null, redactor);
    }
}
