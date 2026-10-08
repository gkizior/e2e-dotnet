// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>
/// Polling assertions for a <see cref="Locator"/>, <c>expect.soft</c>, and
/// <c>expect.poll</c>. A passing locator assertion verifies the previous
/// <c>agent.act</c>. Plain value matchers are not ported: use NUnit
/// <c>Assert.That</c>, and <c>Assert.EnterMultipleScope</c> for soft value checks.
/// </summary>
public static class Expect
{
    public static LocatorExpect That(Locator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        return new LocatorExpect(locator, negated: false);
    }

    /// <summary>
    /// The locator matchers, but a failure is kept on the session instead of
    /// thrown and the body runs on. The test fails at the end with every kept
    /// failure. <c>E2ETest</c> records each one as an NUnit assertion failure.
    /// </summary>
    public static SoftLocatorExpect Soft(Locator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        return new SoftLocatorExpect(new LocatorExpect(locator, negated: false), locator.Screen.SoftFailures);
    }

    /// <summary>Re-reads <paramref name="read"/> until the chosen matcher holds or the timeout passes.</summary>
    public static PollExpectation<T> Poll<T>(Func<CancellationToken, Task<T>> read, PollOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        options ??= new PollOptions();
        PollExpectation<T>.Validate(options);
        return new PollExpectation<T>(read, options, negated: false);
    }

    /// <inheritdoc cref="Poll{T}(Func{CancellationToken, Task{T}}, PollOptions?)"/>
    public static PollExpectation<T> Poll<T>(Func<Task<T>> read, PollOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        return Poll(_ => read(), options);
    }

    /// <inheritdoc cref="Poll{T}(Func{CancellationToken, Task{T}}, PollOptions?)"/>
    public static PollExpectation<T> Poll<T>(Func<T> read, PollOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        return Poll(_ => Task.FromResult(read()), options);
    }
}

/// <summary>
/// Matchers that retry until they pass or the assertion timeout elapses. Each takes an optional
/// <c>timeout</c> that replaces the assertion timeout for that call.
/// </summary>
public sealed class LocatorExpect
{
    /// <summary>How long a negated matcher's condition must stay false before it passes, as upstream.</summary>
    internal static readonly TimeSpan NegationGrace = TimeSpan.FromMilliseconds(1000);

    /// <summary>A read that starts with less than this left of the wait timed out because the wait did, not because the app stopped answering. Upstream's poll tick.</summary>
    private static readonly TimeSpan CutOffWindow = TimeSpan.FromMilliseconds(100);

    private readonly Locator _locator;
    private readonly bool _negated;

    internal LocatorExpect(Locator locator, bool negated)
    {
        _locator = locator;
        _negated = negated;
    }

    /// <summary>Inverts the matcher. A negated matcher passes after its condition has been false for 1000 ms in a row.</summary>
    public LocatorExpect Not => new(_locator, !_negated);

    /// <summary>Waits for one visible match. <paramref name="visible"/> false waits for hidden or absent, as <see cref="ToBeHiddenAsync"/>.</summary>
    public Task ToBeVisibleAsync(bool visible = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return VisibilityAsync("toBeVisible", visible, timeout, cancellationToken);
    }

    /// <summary>Waits for hidden or absent state.</summary>
    public Task ToBeHiddenAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return VisibilityAsync("toBeHidden", visible: false, timeout, cancellationToken);
    }

    /// <summary>Waits for one match to exist, visible or not. <paramref name="attached"/> false waits for none.</summary>
    public Task ToBeAttachedAsync(bool attached = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return PollAsync(
            "toBeAttached",
            attached ? "attached" : "detached",
            timeout,
            includeHidden: true,
            matches => matches.Count > 1
                ? Verdict.Strict(matches.Count)
                : new Verdict((matches.Count == 1) == attached, matches.Count == 1 ? "attached" : "no node"),
            cancellationToken);
    }

    /// <summary>Waits for enabled state. <paramref name="enabled"/> false waits for disabled state.</summary>
    public Task ToBeEnabledAsync(bool enabled = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return StateAsync("toBeEnabled", node => !node.States.Disabled, enabled, enabled ? "enabled" : "disabled", timeout, cancellationToken);
    }

    /// <summary>Waits for disabled state.</summary>
    public Task ToBeDisabledAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return StateAsync("toBeDisabled", node => node.States.Disabled, true, "disabled", timeout, cancellationToken);
    }

    /// <summary>Waits for checked state. <paramref name="isChecked"/> false waits for unchecked state.</summary>
    public Task ToBeCheckedAsync(bool isChecked = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return StateAsync("toBeChecked", node => node.States.Checked, isChecked, isChecked ? "checked" : "unchecked", timeout, cancellationToken);
    }

    /// <summary>Waits for selected state.</summary>
    public Task ToBeSelectedAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return StateAsync("toBeSelected", node => node.States.Selected, true, "selected", timeout, cancellationToken);
    }

    /// <summary>Waits for expanded state.</summary>
    public Task ToBeExpandedAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return StateAsync("toBeExpanded", node => node.States.Expanded, true, "expanded", timeout, cancellationToken);
    }

    /// <summary>Waits for focused state.</summary>
    public Task ToBeFocusedAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return StateAsync("toBeFocused", node => node.States.Focused, true, "focused", timeout, cancellationToken);
    }

    /// <summary>
    /// Waits for exact normalized text: the node's rendered text (<c>innerText</c> on the web), not its label.
    /// <paramref name="ignoreCase"/> folds a string's case, and adds or removes a pattern's.
    /// </summary>
    public Task ToHaveTextAsync(TextMatch expected, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return TextAsync(Field.HasText, expected, ignoreCase, timeout, cancellationToken);
    }

    /// <summary>Waits for exactly as many matches as entries, each with its entry's text, in order.</summary>
    public Task ToHaveTextAsync(IReadOnlyList<TextMatch> expected, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return TextListAsync(Field.HasText, expected, ignoreCase, timeout, cancellationToken);
    }

    /// <summary>Waits for contained normalized text.</summary>
    public Task ToContainTextAsync(TextMatch expected, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return TextAsync(Field.ContainsText, expected, ignoreCase, timeout, cancellationToken);
    }

    /// <summary>Waits for each entry to be contained by a distinct match, in order. Extra matches are allowed.</summary>
    public Task ToContainTextAsync(IReadOnlyList<TextMatch> expected, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return TextListAsync(Field.ContainsText, expected, ignoreCase, timeout, cancellationToken);
    }

    /// <summary>Waits for a form control's value, compared as it is. Fails on a node that is not a form control, negated too.</summary>
    public Task ToHaveValueAsync(TextMatch expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return TextAsync(Field.Value, expected, null, timeout, cancellationToken);
    }

    /// <summary>Waits for an accessible name.</summary>
    public Task ToHaveAccessibleNameAsync(TextMatch expected, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        return TextAsync(Field.Name, expected, ignoreCase, timeout, cancellationToken);
    }

    /// <summary>Waits for the attribute to be present.</summary>
    public Task ToHaveAttributeAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        return AttributeAsync(name, null, null, timeout, cancellationToken);
    }

    /// <summary>Waits for the attribute to be present and its normalized value to match.</summary>
    public Task ToHaveAttributeAsync(string name, TextMatch value, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(value);
        return AttributeAsync(name, value, ignoreCase, timeout, cancellationToken);
    }

    /// <summary>Waits for an exact match count.</summary>
    public Task ToHaveCountAsync(int count, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return PollAsync(
            "toHaveCount",
            "count " + Number(count),
            timeout,
            includeHidden: false,
            matches => new Verdict(matches.Count == count, "count " + Number(matches.Count)),
            cancellationToken);
    }

    private Task VisibilityAsync(string matcher, bool visible, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        // Text queries keep hidden matches, so a match can be hidden. Visible is
        // one match that is not hidden; hidden is no match or one hidden match.
        return PollAsync(
            matcher,
            visible ? "visible" : "hidden or absent",
            timeout,
            includeHidden: false,
            matches =>
            {
                if (visible && matches.Count > 1)
                {
                    return Verdict.Strict(matches.Count);
                }

                var shown = matches.Count == 1 && !matches[0].States.Hidden;
                var observed = matches.Count switch
                {
                    0 => "absent",
                    1 => shown ? "visible" : "hidden",
                    _ => matches.Count.ToString(CultureInfo.InvariantCulture) + " nodes",
                };
                var hidden = matches.Count == 0 || (matches.Count == 1 && !shown);
                return new Verdict(visible ? shown : hidden, observed);
            },
            cancellationToken);
    }

    private Task StateAsync(string matcher, Func<SemanticNode, bool> state, bool expected, string describeExpected, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        return PollSingleAsync(
            matcher,
            describeExpected,
            timeout,
            node => new Verdict(state(node) == expected, ObservedStates(node)),
            cancellationToken);
    }

    private Task TextAsync(Field field, TextMatch expected, bool? ignoreCase, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var pattern = expected.WithIgnoreCase(ignoreCase);
        return PollSingleAsync(
            field.Matcher,
            field.DescribeExpected(pattern.Describe(ignoreCase)),
            timeout,
            node =>
            {
                if (field.ReadsWithheld)
                {
                    _locator.DenySecureRead([node]);
                }

                var actual = field.Read(node);
                if (actual is null)
                {
                    // No sample of a node without the field can answer, so the matcher fails negated or not.
                    return new Verdict(null, "no " + field.Label + " (not a form control)");
                }

                return new Verdict(field.Compare(actual, pattern, ignoreCase), field.Label + " " + field.Print(actual));
            },
            cancellationToken);
    }

    private Task TextListAsync(Field field, IReadOnlyList<TextMatch> expected, bool? ignoreCase, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var patterns = expected.Select(entry =>
        {
            ArgumentNullException.ThrowIfNull(entry, nameof(expected));
            return entry.WithIgnoreCase(ignoreCase);
        }).ToList();
        bool Satisfies(SemanticNode node, TextMatch pattern) => field.Read(node) is { } actual && field.Compare(actual, pattern, ignoreCase);
        return PollAsync(
            field.Matcher,
            field.DescribeExpected("[" + string.Join(", ", patterns.Select(pattern => pattern.Describe(ignoreCase))) + "]"),
            timeout,
            includeHidden: false,
            matches =>
            {
                _locator.DenySecureRead(matches);
                var holds = field.Contains ? MatchesSubsequence(matches, patterns, Satisfies) : MatchesPositionally(matches, patterns, Satisfies);
                return new Verdict(holds, field.Label + " [" + string.Join(", ", matches.Select(node => field.Read(node) is { } actual ? field.Print(actual) : "no " + field.Label)) + "]");
            },
            cancellationToken);
    }

    private Task AttributeAsync(string name, TextMatch? value, bool? ignoreCase, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        var pattern = value?.WithIgnoreCase(ignoreCase);
        return PollSingleAsync(
            "toHaveAttribute",
            pattern is null ? "attribute \"" + name + "\"" : "attribute \"" + name + "\" " + pattern.Describe(ignoreCase),
            timeout,
            node =>
            {
                _locator.DenySecureRead([node]);
                if (node.AttributeOf(name) is not { } attribute)
                {
                    return new Verdict(false, "attribute \"" + name + "\" absent");
                }

                var holds = pattern is null || TextRules.Compare(attribute, pattern, contains: false, normalize: true, ignoreCase);
                return new Verdict(holds, "attribute \"" + name + "\" " + Quote(attribute));
            },
            cancellationToken);
    }

    /// <summary>A matcher that needs exactly one match before its condition means anything.</summary>
    private Task PollSingleAsync(string matcher, string describeExpected, TimeSpan? timeout, Func<SemanticNode, Verdict> check, CancellationToken cancellationToken)
    {
        return PollAsync(
            matcher,
            describeExpected,
            timeout,
            includeHidden: false,
            matches => matches.Count switch
            {
                0 => new Verdict(null, "no node"),
                1 => check(matches[0]),
                _ => Verdict.Strict(matches.Count),
            },
            cancellationToken);
    }

    /// <summary>
    /// Polls until the condition holds, or, negated, until it has been false for <see cref="NegationGrace"/>
    /// in a row (or for the whole budget when that is shorter). A sample that cannot answer resets the
    /// negation clock: <c>not.toBeChecked</c> on no node is not a pass. A negation holds from the moment the
    /// read that first saw it was issued, so a slow read counts toward the window. A read the deadline cut off
    /// (see <see cref="CutOffWindow"/>) after an earlier one completed ends the poll on the last sample: a
    /// negation passes if it had held long enough by then, anything else fails with that read as its cause.
    /// </summary>
    private async Task PollAsync(
        string matcher,
        string describeExpected,
        TimeSpan? timeout,
        bool includeHidden,
        Func<IReadOnlyList<SemanticNode>, Verdict> evaluate,
        CancellationToken cancellationToken)
    {
        if (timeout is { } budget)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(budget, TimeSpan.Zero, nameof(timeout));
        }

        var token = _locator.Screen.Token(cancellationToken);
        var clock = _locator.Screen.Clock;
        var start = clock.GetUtcNow();
        var deadline = start + (timeout ?? _locator.Screen.AssertionTimeout);
        var grace = deadline - start < NegationGrace ? deadline - start : NegationGrace;
        var readAt = start;
        DateTimeOffset? falseSince = null;
        Verdict? last = null;
        IReadOnlyList<SemanticNode> lastMatches = [];
        bool Holds(DateTimeOffset now) => falseSince is { } since && now - since >= grace;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var startedWith = deadline - readAt;
                IReadOnlyList<SemanticNode> matches;
                try
                {
                    matches = await _locator.ResolveAsync(token, includeHidden).ConfigureAwait(false);
                }
                catch (EngineException ex) when (last is not null && ex.Code == EngineErrorCodes.OperationTimeout && startedWith < CutOffWindow)
                {
                    if (_negated && Holds(Min(clock.GetUtcNow(), deadline)))
                    {
                        _locator.Screen.NotifyVerified();
                        return;
                    }

                    throw Failure(matcher, describeExpected, last.Value, lastMatches, ex);
                }

                var verdict = evaluate(matches);
                last = verdict;
                lastMatches = matches;
                if (!_negated && verdict.Holds == true)
                {
                    _locator.Screen.NotifyVerified();
                    return;
                }

                // False from the first sample means false for the whole budget so far, so the clock starts with the budget.
                falseSince = _negated && verdict.Holds == false ? falseSince ?? readAt : null;
                var now = clock.GetUtcNow();
                if (Holds(now))
                {
                    _locator.Screen.NotifyVerified();
                    return;
                }

                if (now >= deadline)
                {
                    throw Failure(matcher, describeExpected, verdict, matches);
                }

                // Rounded up to whole milliseconds, which is what Task.Delay waits, so the capped pause never wakes before the deadline.
                var remaining = TimeSpan.FromMilliseconds(Math.Ceiling((deadline - now).TotalMilliseconds));
                await Task.Delay(_negated && remaining < _locator.Screen.PollInterval ? remaining : _locator.Screen.PollInterval, clock, token).ConfigureAwait(false);

                // A read past the deadline has no budget left, so a negation decides at the deadline on what it has seen.
                readAt = clock.GetUtcNow();
                if (_negated && readAt >= deadline)
                {
                    if (Holds(readAt))
                    {
                        _locator.Screen.NotifyVerified();
                        return;
                    }

                    throw Failure(matcher, describeExpected, verdict, matches);
                }
            }
        }
        catch (OperationCanceledException ex) when (token.IsCancellationRequested)
        {
            throw new TestException(EngineErrorCodes.Cancelled, "operation cancelled", ex);
        }
    }

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    /// <summary>The error a matcher throws at its deadline, built from its last sample.</summary>
    private TestException Failure(string matcher, string describeExpected, Verdict verdict, IReadOnlyList<SemanticNode> matches, Exception? cause = null)
    {
        var name = (_negated ? "not." : "") + matcher;
        var code = verdict.StrictCount > 1 ? "STRICT_MODE" : "ASSERTION_FAILED";
        var message = "expect(" + _locator.Query.Describe() + ")." + name + " failed: expected " + (_negated ? "not " : "") + describeExpected
            + "; observed " + verdict.Observed + " (match count " + Number(matches.Count) + ")";
        return cause is null ? new TestException(code, message) : new TestException(code, message, cause);
    }

    private static bool MatchesPositionally(IReadOnlyList<SemanticNode> nodes, List<TextMatch> patterns, Func<SemanticNode, TextMatch, bool> satisfies)
    {
        if (nodes.Count != patterns.Count)
        {
            return false;
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            if (!satisfies(nodes[i], patterns[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Whether every pattern is satisfied by a distinct node, in order. Greedy, which finds a subsequence whenever one exists.</summary>
    private static bool MatchesSubsequence(IReadOnlyList<SemanticNode> nodes, List<TextMatch> patterns, Func<SemanticNode, TextMatch, bool> satisfies)
    {
        var next = 0;
        foreach (var node in nodes)
        {
            if (next == patterns.Count)
            {
                break;
            }

            if (satisfies(node, patterns[next]))
            {
                next++;
            }
        }

        return next == patterns.Count;
    }

    private static string ObservedStates(SemanticNode node)
    {
        var states = node.States;
        var active = new List<string>();
        void Add(bool on, string name)
        {
            if (on)
            {
                active.Add(name);
            }
        }

        Add(states.Checked, "checked");
        Add(states.Disabled, "disabled");
        Add(states.Expanded, "expanded");
        Add(states.Focused, "focused");
        Add(states.Hidden, "hidden");
        Add(states.Secure, "secure");
        Add(states.Selected, "selected");
        Add(states.Pressed, "pressed");
        return active.Count == 0 ? "default states" : "states: " + string.Join(", ", active);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Quote(string? value) => "\"" + (value ?? "") + "\"";

    /// <summary>One sample's answer: <see cref="Holds"/> is null when the sample cannot answer, such as no node or several.</summary>
    private readonly record struct Verdict(bool? Holds, string Observed, int StrictCount = 0)
    {
        public static Verdict Strict(int count) => new(null, "matched " + Number(count) + " nodes", count);
    }

    /// <summary>The node field a text matcher reads, and how it compares.</summary>
    private sealed class Field
    {
        public static readonly Field HasText = new("toHaveText", "text", contains: false, normalize: true, readsWithheld: true, pattern => "text " + pattern);

        public static readonly Field ContainsText = new("toContainText", "text", contains: true, normalize: true, readsWithheld: true, pattern => "text containing " + pattern);

        public static readonly Field Value = new("toHaveValue", "value", contains: false, normalize: false, readsWithheld: true, pattern => "value " + pattern);

        public static readonly Field Name = new("toHaveAccessibleName", "accessible name", contains: false, normalize: true, readsWithheld: false, pattern => "accessible name " + pattern);

        /// <summary>
        /// Roles whose node carries a value: the editable roles plus the controls a platform reports a value
        /// for without taking typed text (a range input, a number stepper, a multiple select and its options).
        /// </summary>
        private static readonly HashSet<string> ValueRoles = ["textbox", "searchbox", "combobox", "spinbutton", "slider", "listbox", "option"];

        private readonly bool _normalize;

        private Field(string matcher, string label, bool contains, bool normalize, bool readsWithheld, Func<string, string> describeExpected)
        {
            Matcher = matcher;
            Label = label;
            Contains = contains;
            _normalize = normalize;
            ReadsWithheld = readsWithheld;
            DescribeExpected = describeExpected;
        }

        public string Matcher { get; }

        public string Label { get; }

        public bool Contains { get; }

        public bool ReadsWithheld { get; }

        public Func<string, string> DescribeExpected { get; }

        /// <summary>
        /// The field as the matcher reads it. Text and name read as the empty string when absent. The web engine
        /// reports text for every node, empty for an icon button, so text falls back to the name only for a node
        /// with no text at all, as the document engine's controls have. A value reads as the empty string only on
        /// a control whose role carries one, because an engine omits an empty value; any other node is not a form
        /// control and has no value (null).
        /// </summary>
        public string? Read(SemanticNode node)
        {
            if (ReferenceEquals(this, Value))
            {
                return node.Value ?? (ValueRoles.Contains(node.Role ?? "") ? "" : null);
            }

            if (ReferenceEquals(this, Name))
            {
                return node.Name ?? "";
            }

            return node.Text ?? node.Name ?? "";
        }

        public bool Compare(string actual, TextMatch pattern, bool? ignoreCase)
        {
            return TextRules.Compare(actual, pattern, Contains, _normalize, ignoreCase);
        }

        public string Print(string actual) => Quote(_normalize ? TextRules.Normalize(actual) : actual);
    }
}
