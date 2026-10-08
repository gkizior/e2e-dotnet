// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E;

/// <summary>
/// How far one look settles.
/// <list type="bullet">
/// <item><see cref="Raw"/>: the screen as it is, for the looks between retries.</item>
/// <item><see cref="AfterChange"/>: waits for the screen to leave the previous action's shape, then reads the first
/// capture after it. Enough after a fill, whose effect is confined to the field it acted on.</item>
/// <item><see cref="HeldStill"/>: after the change wait, captures a beat apart until two agree in shape. Needed after
/// an action that can submit, open, move, or replace the screen; without it a replayed action lands on a form
/// mid-clear or a list mid-update and commits something the recorded run never did.</item>
/// </list>
/// </summary>
internal enum SettleMode
{
    Raw,
    AfterChange,
    HeldStill,
}

/// <summary>
/// What happens to the screen after one kind of action, as far as the runtime waits for it. <see cref="ChangeWait"/>
/// is how long after the action the screen has to leave the shape it was resolved against; capture time and delays
/// before the next settled look count toward it. It is null for an action whose effect the tree cannot show: a secret
/// fill is masked out of every capture, so a change wait after it could never be satisfied. <see cref="Look"/> is how
/// far the replay's look before the next action settles.
/// </summary>
internal readonly record struct SettleAfter(TimeSpan? ChangeWait, SettleMode Look);

/// <summary>
/// How the screen is read after each kind of action. One table, keyed by the recorded action kind, answers both
/// questions the runtime asks once an action has committed: how long after it the screen has to leave the shape the
/// action was resolved against, and how far the replay's look before the next action settles. The live loop and
/// replay read the same table, so they can never disagree about the same action.
/// </summary>
internal static class SettlePolicy
{
    /// <summary>
    /// How long a held-still look proves the new shape holds: captures a poll apart until two agree, bounded so a
    /// screen that keeps moving costs one wait, not the step.
    /// </summary>
    public static readonly TimeSpan HeldStill = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long after an action a settled look waits for the screen to move away from the shape the action was
    /// resolved against before accepting that the action changed nothing visible. A tap on a link starts a navigation
    /// that commits hundreds of milliseconds later; a submit renders its result after a round trip. Read too early,
    /// the observation is the old page, stable and wrong.
    /// </summary>
    private static readonly TimeSpan FullChangeWait = TimeSpan.FromSeconds(2);

    /// <summary>
    /// The change wait after a scroll. A scroll moves nothing the tree records most of the time, so a long wait is
    /// pure cost; a windowed list rendering its next rows does so within a few hundred milliseconds.
    /// </summary>
    private static readonly TimeSpan BriefChangeWait = TimeSpan.FromMilliseconds(500);

    private static readonly SettleAfter Moves = new(FullChangeWait, SettleMode.HeldStill);

    private static readonly SettleAfter Types = new(FullChangeWait, SettleMode.AfterChange);

    private static readonly SettleAfter Scrolls = new(BriefChangeWait, SettleMode.HeldStill);

    private static readonly SettleAfter Masked = new(null, SettleMode.HeldStill);

    private static readonly Dictionary<string, SettleAfter> After = new(StringComparer.Ordinal)
    {
        ["tap"] = Moves,
        ["doubleTap"] = Moves,
        ["press"] = Moves,
        ["select"] = Moves,
        ["check"] = Moves,
        ["uncheck"] = Moves,
        ["navigate"] = Moves,
        ["back"] = Moves,
        ["fill"] = Types,
        ["clear"] = Types,
        ["scroll"] = Scrolls,
        ["scrollTo"] = Scrolls,
        ["scrollUntil"] = Scrolls,
    };

    /// <summary>The settle policy of one recorded action. A secret fill is recorded as a fill of its placeholder.</summary>
    public static SettleAfter For(RecordedAction action)
    {
        if (string.Equals(action.Kind, "fill", StringComparison.Ordinal) && action.Value is { } value && value.StartsWith("<secret:", StringComparison.Ordinal))
        {
            return Masked;
        }

        return After.TryGetValue(action.Kind, out var after) ? after : Moves;
    }
}
