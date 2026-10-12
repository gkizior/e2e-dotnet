// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Internal;

/// <summary>A point in time a wait must end by, as upstream's <c>Deadline</c>.</summary>
internal sealed class Deadline(DateTimeOffset endsAt)
{
    public Deadline(TimeSpan timeout, TimeProvider clock)
        : this(clock.GetUtcNow() + timeout)
    {
    }

    public DateTimeOffset EndsAt { get; } = endsAt;

    /// <summary>Remaining budget, never negative.</summary>
    public TimeSpan Remaining(DateTimeOffset now) => now >= EndsAt ? TimeSpan.Zero : EndsAt - now;

    public bool Expired(DateTimeOffset now) => now >= EndsAt;
}

/// <summary>What <see cref="Time.PollConditionAsync"/> polls, and how it ends.</summary>
internal sealed class PollConditionOptions
{
    public required Deadline Deadline { get; init; }

    public required TimeProvider Clock { get; init; }

    public required CancellationToken Token { get; init; }

    public required bool Negated { get; init; }

    /// <summary>The longest pause between reads.</summary>
    public TimeSpan Interval { get; init; } = Time.PollInterval;

    /// <summary>
    /// Evaluates the positive condition once, every read bounded by the deadline: a read on a longer budget outlasts
    /// the poll. Returns null when the condition cannot be evaluated yet: the positive poll keeps waiting and the
    /// negation grace window resets.
    /// </summary>
    public required Func<Task<bool?>> Evaluate { get; init; }

    /// <summary>Builds the poll's failure; the argument is the read the deadline cut off, when one did.</summary>
    public required Func<Exception?, Exception> OnTimeout { get; init; }
}

/// <summary>Deadline and polling helpers, as upstream's <c>internal/time.ts</c>.</summary>
internal static class Time
{
    /// <summary>Canonical polling cadence for locator and assertion loops.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>How long a negated assertion must hold before it passes.</summary>
    public static readonly TimeSpan NegationGrace = TimeSpan.FromMilliseconds(1000);

    /// <summary>
    /// Whether <paramref name="cause"/> is a read a wait's deadline cut off: an operation timeout of a read that
    /// started with less than one poll tick of the wait left, so it timed out because the wait did, not because the
    /// app stopped answering. A read that started with more and still timed out hung, and its timeout is the failure.
    /// </summary>
    public static bool CutOffAtDeadline(Exception cause, TimeSpan startedWith) =>
        startedWith < PollInterval && cause is EngineException { Code: EngineErrorCodes.OperationTimeout };

    /// <summary>
    /// Polls a condition until it holds (or, when negated, until its negation has held continuously for the negation
    /// grace window), throwing the caller's error at the deadline. No sleep runs past the deadline, so the last read
    /// starts at it and never sees a state that arrived later.
    /// A negation holds from the moment the read that first saw it was issued, so a slow read counts toward the
    /// window. A budget shorter than the window still has to be satisfiable: the negation then only needs to hold for
    /// the whole budget, and passes at the deadline on what it has seen, since a read past the deadline has no budget
    /// left. A read the deadline cut off (see <see cref="CutOffAtDeadline"/>) after an earlier one completed saw
    /// nothing: the poll ends there, and a negation passes if it had held long enough by then.
    /// </summary>
    public static async Task PollConditionAsync(PollConditionOptions options)
    {
        var (deadline, clock, token, negated) = (options.Deadline, options.Clock, options.Token, options.Negated);
        var startedAt = clock.GetUtcNow();
        var grace = deadline.Remaining(startedAt) < NegationGrace ? deadline.Remaining(startedAt) : NegationGrace;
        var readAt = startedAt;
        DateTimeOffset? holdingSince = null;
        var sampled = false;
        bool Holds(DateTimeOffset now) => holdingSince is { } since && now - since >= grace;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var startedWith = deadline.Remaining(readAt);
            bool? value;
            try
            {
                value = await options.Evaluate().ConfigureAwait(false);
                sampled = true;
            }
            catch (EngineException ex) when (sampled && CutOffAtDeadline(ex, startedWith))
            {
                var now = clock.GetUtcNow();
                if (negated && Holds(now < deadline.EndsAt ? now : deadline.EndsAt))
                {
                    return;
                }

                throw options.OnTimeout(ex);
            }

            if (!negated)
            {
                if (value == true)
                {
                    return;
                }
            }
            else
            {
                // False from the first sample means false for the whole budget so far, so the clock starts with the budget.
                holdingSince = value == false ? holdingSince ?? readAt : null;
            }

            var current = clock.GetUtcNow();
            if (Holds(current))
            {
                return;
            }

            if (deadline.Expired(current))
            {
                throw options.OnTimeout(null);
            }

            // Rounded up to whole milliseconds, which is what Task.Delay waits, so the capped pause never wakes before the deadline.
            var remaining = TimeSpan.FromMilliseconds(Math.Ceiling(deadline.Remaining(current).TotalMilliseconds));
            await Task.Delay(remaining < options.Interval ? remaining : options.Interval, clock, token).ConfigureAwait(false);

            // A read past the deadline has no budget left, so a negation decides at the deadline on what it has seen.
            readAt = clock.GetUtcNow();
            if (negated && deadline.Expired(readAt))
            {
                if (Holds(readAt))
                {
                    return;
                }

                throw options.OnTimeout(null);
            }
        }
    }
}
