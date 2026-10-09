// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>
/// The shape the screen had when the preceding action was resolved, and the deadline for the screen to leave it: the
/// action's settle policy (<see cref="SettlePolicy"/>) decides the window, armed once the action commits.
/// </summary>
internal sealed record PendingChange(string Shape, DateTimeOffset Deadline);

/// <summary>The waits of one settle, decided by the caller; the loop itself keeps no defaults for them.</summary>
internal sealed class SettleOptions<T>
{
    /// <summary>
    /// The pre-action shape to leave first, when an action is pending. The settle waits, bounded by its window, for
    /// the shape to differ from it, so an action's result is read after its effect rather than before.
    /// </summary>
    public PendingChange? ChangedFrom { get; init; }

    /// <summary>
    /// Whether a capture is a screen in transition rather than a screen: an empty document between two pages, say.
    /// After an action, such a capture satisfies neither the change wait nor the stability check.
    /// </summary>
    public Func<T, bool>? Transitional { get; init; }

    /// <summary>
    /// How long the loop proves the new shape holds still: captures a poll apart until two agree, or this runs out.
    /// Zero reads the first capture past the change wait as it is.
    /// </summary>
    public TimeSpan StableWait { get; init; }

    public TimeSpan? Poll { get; init; }
}

/// <summary>An observation as the settle loop reads it: its shape, whether it is a screen in transition, and the loop itself.</summary>
internal static class AgentObservation
{
    /// <summary>Poll interval for shape-stability settling.</summary>
    private static readonly TimeSpan SettlePoll = TimeSpan.FromMilliseconds(75);

    private static readonly Regex Reference = new(@" \[ref=[^\]]*\]", RegexOptions.CultureInvariant);

    private static readonly Regex Focus = new(@" \[focused\]", RegexOptions.CultureInvariant);

    /// <summary><c>12:05</c>, <c>0:59</c>, <c>23:59:59</c>: a value that changes on its own once a second or minute.</summary>
    private static readonly Regex Clock = new(@"\b\d{1,2}:\d{2}(?::\d{2})?\b", RegexOptions.CultureInvariant);

    private static readonly HashSet<string> EmptyRoots = new(["document", "screen", "window"], StringComparer.Ordinal);

    /// <summary>
    /// The shape two looks are compared by: the screen as the model reads it, without the per-observation node
    /// references, without focus, which moves without the page changing, and with clock-like values read as one
    /// placeholder, which tick without the page changing. The scroll position counts too: upstream's screenshot shows a
    /// scroll the tree does not, and the port's observations carry no screenshot.
    /// </summary>
    public static string Shape(Observation observation) => Shape(SnapshotText.Render(observation, Redactor.None)) + "\n" + observation.ScrollPosition;

    /// <summary>The shape of a screen already rendered as text.</summary>
    public static string Shape(string text) => Clock.Replace(Focus.Replace(Reference.Replace(text, ""), ""), "<time>");

    /// <summary>Whether an observation shows a screen in transition: no node, or only an empty document root.</summary>
    public static bool IsTransitional(Observation observation)
    {
        var visible = observation.Roots.Where(root => !(root.States.Hidden || root.States.AriaHidden)).ToList();
        return visible.Count == 0
            || (visible.Count == 1 && visible[0].Role is { } role && EmptyRoots.Contains(role) && visible[0].Children.All(child => child.States.Hidden || child.States.AriaHidden));
    }

    /// <summary>
    /// Captures until the screen shape holds still, bounded by a short ceiling and the step's cancellation. An
    /// observation taken right after an action can be a snapshot the app is still reacting to, and acting or judging
    /// on it repeats actions and passes steps on pre-render state.
    /// <para>
    /// With <see cref="SettleOptions{T}.ChangedFrom"/>, the loop has two phases: first it waits for the shape to leave
    /// the pre-action one (a navigation committing, a route swapping the body, a submit rendering), then it waits for
    /// the new shape to hold still. A screen that never leaves the pre-action shape is returned as it is once the
    /// change wait runs out: the caller reports it unchanged rather than guessing. Upstream also returns at once on a
    /// capture with no comparable shape, a screenshot alone; the port's observations always have one.
    /// </para>
    /// </summary>
    public static async Task<T> SettleAsync<T>(
        Func<CancellationToken, Task<T>> capture,
        Func<T, string> shapeOf,
        SettleOptions<T> options,
        TimeProvider time,
        CancellationToken token)
    {
        var poll = options.Poll ?? SettlePoll;
        var transitional = options.Transitional ?? (_ => false);
        var value = await capture(token).ConfigureAwait(false);
        var shape = shapeOf(value);
        if (options.ChangedFrom is { } changedFrom)
        {
            while ((string.Equals(shape, changedFrom.Shape, StringComparison.Ordinal) || transitional(value))
                && time.GetUtcNow() < changedFrom.Deadline)
            {
                await Task.Delay(poll, time, token).ConfigureAwait(false);
                value = await capture(token).ConfigureAwait(false);
                shape = shapeOf(value);
            }
        }

        var deadline = time.GetUtcNow() + options.StableWait;
        while (time.GetUtcNow() < deadline)
        {
            await Task.Delay(poll, time, token).ConfigureAwait(false);
            value = await capture(token).ConfigureAwait(false);
            var next = shapeOf(value);
            var stable = string.Equals(next, shape, StringComparison.Ordinal);
            shape = next;
            if (stable && (options.ChangedFrom is null || !transitional(value)))
            {
                break;
            }
        }

        return value;
    }
}
