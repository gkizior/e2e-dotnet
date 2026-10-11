// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace E2E;

/// <summary>
/// Detects a model going in circles: the same call again and again, or a short sequence of calls
/// repeated. Identity is the tool name plus its exact arguments, so two taps on different targets are
/// never a repeat. A warning lets the model react; a stop forces the verdict.
/// </summary>
internal static class LoopGuard
{
    private const int MaxCyclePeriod = 4;
    private const int RepeatWarn = 3;
    private const int RepeatStop = 5;
    private const int CycleWarn = 2;
    private const int CycleStop = 3;

    /// <summary>The calls of a transcript in order, without the verdict tool: ending the step is never a loop.</summary>
    public static IReadOnlyList<(string Name, string Input)> Calls(IEnumerable<ModelMessage> messages) =>
        messages
            .Where(message => message.ToolCalls is not null)
            .SelectMany(message => message.ToolCalls!)
            .Where(call => !string.Equals(call.Name, "done", StringComparison.Ordinal))
            .Select(call => (call.Name, call.Arguments.GetRawText()))
            .ToList();

    /// <summary>
    /// Checks the trailing calls for exact repetition (period 1) and short exact cycles (periods 2 to 4).
    /// Returns no reason when the calls are clear.
    /// </summary>
    public static (bool Stop, string? Reason) Check(IReadOnlyList<(string Name, string Input)> calls)
    {
        (bool Stop, string? Reason) warning = (false, null);
        for (var period = 1; period <= MaxCyclePeriod; period++)
        {
            var repeats = TrailingRepeats(calls, period);
            var (warnAt, stopAt) = period == 1 ? (RepeatWarn, RepeatStop) : (CycleWarn, CycleStop);
            if (repeats >= stopAt)
            {
                return (true, Describe(calls, period, repeats));
            }

            if (repeats >= warnAt && warning.Reason is null)
            {
                warning = (false, Describe(calls, period, repeats));
            }
        }

        return warning;
    }

    /// <summary>How many times the last <paramref name="period"/> calls repeat one after the other.</summary>
    private static int TrailingRepeats(IReadOnlyList<(string Name, string Input)> calls, int period)
    {
        if (calls.Count < period)
        {
            return 0;
        }

        var last = calls.Count - period;

        // A cycle of one call repeated is period 1's, and period 1 applies the stricter policy.
        if (period > 1 && Enumerable.Range(1, period - 1).All(index => calls[last + index] == calls[last]))
        {
            return 0;
        }

        var repeats = 1;
        for (var offset = period; last - offset >= 0; offset += period)
        {
            if (Enumerable.Range(0, period).Any(index => calls[last + index] != calls[last - offset + index]))
            {
                break;
            }

            repeats++;
        }

        return repeats;
    }

    private static string Describe(IReadOnlyList<(string Name, string Input)> calls, int period, int repeats)
    {
        var names = string.Join(", ", calls.Skip(calls.Count - period).Select(call => call.Name));
        var count = repeats.ToString(CultureInfo.InvariantCulture);
        return period == 1
            ? "the same \"" + names + "\" call with identical input was issued " + count + " times in a row"
            : "the same " + period.ToString(CultureInfo.InvariantCulture) + "-call sequence (" + names + ") repeated " + count + " times with identical inputs";
    }
}
