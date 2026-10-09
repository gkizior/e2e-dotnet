// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>
/// Semantic queries against the current screen. An action or a single-node
/// assertion fails when the query matches more than one node. Narrow it with
/// <see cref="Locator.Filter(TextMatch)"/>, <see cref="Locator.First"/>, <see cref="Locator.Last"/>, or <see cref="Locator.Nth"/>.
/// </summary>
public sealed class Screen
{
    private readonly Func<CancellationToken, Task<Observation>> _observe;
    private readonly Func<SemanticNode, LocatorAction, CancellationToken, Task> _perform;
    private readonly Func<CancellationToken> _cancellation;
    private readonly Action _verified;

    internal Screen(
        Func<CancellationToken, Task<Observation>> observe,
        Func<SemanticNode, LocatorAction, CancellationToken, Task> perform,
        Func<CancellationToken> cancellation,
        Action verified,
        TimeSpan actionTimeout,
        TimeSpan assertionTimeout,
        SoftFailures? softFailures = null,
        TimeProvider? clock = null)
    {
        SoftFailures = softFailures ?? new SoftFailures();
        _observe = observe;
        _perform = perform;
        _cancellation = cancellation;
        _verified = verified;
        ActionTimeout = actionTimeout;
        AssertionTimeout = assertionTimeout;
        Clock = clock ?? TimeProvider.System;
    }

    internal TimeSpan ActionTimeout { get; }

    internal TimeSpan AssertionTimeout { get; }

    internal TimeProvider Clock { get; }

    internal SoftFailures SoftFailures { get; }

    internal TimeSpan PollInterval { get; } = TimeSpan.FromMilliseconds(50);

    /// <summary>Where <c>ToHaveScreenshotAsync</c> keeps screenshots; null for a screen outside a session.</summary>
    internal ScreenshotStore? Screenshots { get; set; }

    /// <summary>A secret was filled in this attempt, so the app may show it outside a secure field and pixels are withheld.</summary>
    internal bool SecretFilled { get; private set; }

    internal void MarkSecretFilled() => SecretFilled = true;

    /// <summary>Creates a lazy role query. <c>img</c> is read as <c>image</c>. A role query never matches a hidden node.</summary>
    public Locator GetByRole(string role, TextMatch? name = null, bool exact = true)
    {
        return new Locator(this, LocatorQuery.ForRole(role, name, new RoleOptions { Exact = exact }));
    }

    /// <summary>Creates a lazy role query narrowed by name, states, or heading level.</summary>
    public Locator GetByRole(string role, RoleOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Locator(this, LocatorQuery.ForRole(role, options.Name, options));
    }

    /// <summary>Creates a lazy role query narrowed to an accessible name and the given options.</summary>
    public Locator GetByRole(string role, TextMatch name, RoleOptions options)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(options);
        if (options.Name is not null)
        {
            throw new TestException("INVALID_LOCATOR", "getByRole takes the \"name\" as its second argument or in the options, not both.");
        }

        return new Locator(this, LocatorQuery.ForRole(role, name, options));
    }

    /// <summary>
    /// Creates a lazy visible-text query. It answers with the innermost match, such as a
    /// <c>&lt;strong&gt;</c> inside a paragraph. Text inside a control's label answers with the control.
    /// </summary>
    public Locator GetByText(TextMatch text, bool exact = true) => GetByText(text, new TextMatchOptions { Exact = exact });

    /// <summary>
    /// Creates a lazy visible-text query. It answers with the innermost match, such as a
    /// <c>&lt;strong&gt;</c> inside a paragraph. Text inside a control's label answers with the control.
    /// </summary>
    public Locator GetByText(TextMatch text, TextMatchOptions options) => new(this, LocatorQuery.For("text", text, options, nameof(text)));

    /// <summary>Creates a lazy accessible-label query: any node whose accessible name matches.</summary>
    public Locator GetByLabel(TextMatch label, bool exact = true) => GetByLabel(label, new TextMatchOptions { Exact = exact });

    /// <summary>Creates a lazy accessible-label query: any node whose accessible name matches.</summary>
    public Locator GetByLabel(TextMatch label, TextMatchOptions options) => new(this, LocatorQuery.For("label", label, options, nameof(label)));

    /// <summary>Creates a lazy test-id query. A string matches the whole id, case-sensitive.</summary>
    public Locator GetByTestId(TextMatch testId) => GetByTestId(testId, new TextMatchOptions());

    /// <summary>Creates a lazy test-id query. A string matches the whole id, case-sensitive; <see cref="TextMatchOptions.Exact"/> is ignored.</summary>
    public Locator GetByTestId(TextMatch testId, TextMatchOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Locator(this, LocatorQuery.For("testid", testId, new TextMatchOptions { Visible = options.Visible }, nameof(testId)));
    }

    /// <summary>Creates a lazy placeholder query.</summary>
    public Locator GetByPlaceholder(TextMatch placeholder, bool exact = true) => GetByPlaceholder(placeholder, new TextMatchOptions { Exact = exact });

    /// <summary>Creates a lazy placeholder query.</summary>
    public Locator GetByPlaceholder(TextMatch placeholder, TextMatchOptions options) => new(this, LocatorQuery.For("placeholder", placeholder, options, nameof(placeholder)));

    /// <summary>Creates a lazy displayed-value query: the current value of an input, text area, or select. A checkbox or radio never shows its value, so it never matches.</summary>
    public Locator GetByDisplayValue(TextMatch value, bool exact = true) => GetByDisplayValue(value, new TextMatchOptions { Exact = exact });

    /// <summary>Creates a lazy displayed-value query: the current value of an input, text area, or select. A checkbox or radio never shows its value, so it never matches.</summary>
    public Locator GetByDisplayValue(TextMatch value, TextMatchOptions options) => new(this, LocatorQuery.For("displayValue", value, options, nameof(value)));

    internal async Task<Observation> ObserveAsync(CancellationToken cancellationToken)
    {
        return await AppNotOpen.GuardAsync(() => _observe(cancellationToken == default ? _cancellation() : cancellationToken)).ConfigureAwait(false);
    }

    internal async Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken)
    {
        await AppNotOpen.GuardAsync(() => _perform(node, action, cancellationToken == default ? _cancellation() : cancellationToken)).ConfigureAwait(false);
    }

    internal CancellationToken Token(CancellationToken cancellationToken)
    {
        return cancellationToken == default ? _cancellation() : cancellationToken;
    }

    internal void NotifyVerified() => _verified();
}

/// <summary>A query that resolves against a fresh observation every time it is used.</summary>
public sealed class Locator
{
    private readonly Screen _screen;

    internal Locator(Screen screen, LocatorQuery query)
    {
        _screen = screen;
        Query = query;
    }

    internal LocatorQuery Query { get; }

    /// <summary>Keeps the matches whose own or descendants' name, text, or value contains <paramref name="hasText"/>, ignoring case.</summary>
    public Locator Filter(TextMatch hasText)
    {
        ArgumentNullException.ThrowIfNull(hasText);
        hasText.ThrowIfBlank(nameof(hasText));
        return new Locator(_screen, new LocatorQuery { Kind = "filter", Source = Query, HasText = hasText });
    }

    /// <summary>Keeps the matches with a descendant that <paramref name="has"/> matches. <paramref name="has"/> resolves inside each match.</summary>
    public Locator Filter(Locator has)
    {
        ArgumentNullException.ThrowIfNull(has);
        return new Locator(_screen, new LocatorQuery { Kind = "filter", Source = Query, Has = has.Query });
    }

    public Locator First() => Nth(0);

    public Locator Last() => new(_screen, new LocatorQuery { Kind = "index", Source = Query, Index = LocatorQuery.LastIndex });

    public Locator Nth(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new Locator(_screen, new LocatorQuery { Kind = "index", Source = Query, Index = index });
    }

    /// <summary>Creates a role query among this locator's descendants.</summary>
    public Locator GetByRole(string role, TextMatch? name = null, bool exact = true) => Scope(_screen.GetByRole(role, name, exact));

    /// <summary>Creates a role query among this locator's descendants.</summary>
    public Locator GetByRole(string role, RoleOptions options) => Scope(_screen.GetByRole(role, options));

    /// <summary>Creates a role query among this locator's descendants.</summary>
    public Locator GetByRole(string role, TextMatch name, RoleOptions options) => Scope(_screen.GetByRole(role, name, options));

    /// <summary>Creates a visible-text query among this locator's descendants.</summary>
    public Locator GetByText(TextMatch text, bool exact = true) => Scope(_screen.GetByText(text, exact));

    /// <summary>Creates a visible-text query among this locator's descendants.</summary>
    public Locator GetByText(TextMatch text, TextMatchOptions options) => Scope(_screen.GetByText(text, options));

    /// <summary>Creates a label query among this locator's descendants.</summary>
    public Locator GetByLabel(TextMatch label, bool exact = true) => Scope(_screen.GetByLabel(label, exact));

    /// <summary>Creates a label query among this locator's descendants.</summary>
    public Locator GetByLabel(TextMatch label, TextMatchOptions options) => Scope(_screen.GetByLabel(label, options));

    /// <summary>Creates a test-id query among this locator's descendants.</summary>
    public Locator GetByTestId(TextMatch testId) => Scope(_screen.GetByTestId(testId));

    /// <summary>Creates a test-id query among this locator's descendants.</summary>
    public Locator GetByTestId(TextMatch testId, TextMatchOptions options) => Scope(_screen.GetByTestId(testId, options));

    /// <summary>Creates a placeholder query among this locator's descendants.</summary>
    public Locator GetByPlaceholder(TextMatch placeholder, bool exact = true) => Scope(_screen.GetByPlaceholder(placeholder, exact));

    /// <summary>Creates a placeholder query among this locator's descendants.</summary>
    public Locator GetByPlaceholder(TextMatch placeholder, TextMatchOptions options) => Scope(_screen.GetByPlaceholder(placeholder, options));

    /// <summary>Creates a displayed-value query among this locator's descendants.</summary>
    public Locator GetByDisplayValue(TextMatch value, bool exact = true) => Scope(_screen.GetByDisplayValue(value, exact));

    /// <summary>Creates a displayed-value query among this locator's descendants.</summary>
    public Locator GetByDisplayValue(TextMatch value, TextMatchOptions options) => Scope(_screen.GetByDisplayValue(value, options));

    public Task TapAsync(ActionOptions? options = null, CancellationToken cancellationToken = default)
    {
        return ActAsync(new LocatorAction.Tap(), options, cancellationToken);
    }

    /// <summary>Alias of <see cref="TapAsync"/>.</summary>
    public Task ClickAsync(ActionOptions? options = null, CancellationToken cancellationToken = default) => TapAsync(options, cancellationToken);

    public Task DoubleTapAsync(ActionOptions? options = null, CancellationToken cancellationToken = default)
    {
        return ActAsync(new LocatorAction.DoubleTap(), options, cancellationToken);
    }

    /// <summary>Alias of <see cref="DoubleTapAsync"/>.</summary>
    public Task DblClickAsync(ActionOptions? options = null, CancellationToken cancellationToken = default) => DoubleTapAsync(options, cancellationToken);

    public Task FillAsync(string value, ActionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ActAsync(new LocatorAction.Fill(value), options, cancellationToken);
    }

    /// <summary>Fills a secret. The engine marks the value sensitive and never reports it.</summary>
    public Task FillAsync(Secret value, ActionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        _screen.MarkSecretFilled();
        return ActAsync(new LocatorAction.Fill(value.Value, Sensitive: true), options, cancellationToken);
    }

    /// <summary>
    /// Focuses the input and types <paramref name="text"/> one character at a time,
    /// so the app receives key events. <see cref="PressSequentiallyOptions.Delay"/> waits between characters.
    /// </summary>
    public Task PressSequentiallyAsync(string text, PressSequentiallyOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (options?.Delay is TimeSpan wait)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(wait, TimeSpan.Zero, nameof(options));
        }

        return ActAsync(new LocatorAction.PressSequentially(text, options?.Delay), options, cancellationToken);
    }

    public Task PressAsync(string key, ActionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return ActAsync(new LocatorAction.Press(key), options, cancellationToken);
    }

    public Task CheckAsync(ActionOptions? options = null, CancellationToken cancellationToken = default)
    {
        return ActAsync(new LocatorAction.Check(), options, cancellationToken);
    }

    public Task UncheckAsync(ActionOptions? options = null, CancellationToken cancellationToken = default)
    {
        return ActAsync(new LocatorAction.Uncheck(), options, cancellationToken);
    }

    public Task ClearAsync(ActionOptions? options = null, CancellationToken cancellationToken = default)
    {
        return ActAsync(new LocatorAction.Clear(), options, cancellationToken);
    }

    /// <summary>Selects the option whose value or label is <paramref name="value"/>.</summary>
    public Task SelectOptionAsync(string value, ActionOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ActAsync(new LocatorAction.Select(value), options, cancellationToken);
    }

    public Task FocusAsync(ActionOptions? options = null, CancellationToken cancellationToken = default)
    {
        return ActAsync(new LocatorAction.Focus(), options, cancellationToken);
    }

    public Task ScrollIntoViewAsync(ActionOptions? options = null, CancellationToken cancellationToken = default)
    {
        return ActAsync(new LocatorAction.ScrollIntoView(), options, cancellationToken);
    }

    /// <summary>
    /// Reads whitespace-normalized text once: the node's rendered text (<c>innerText</c> on the web), not its label.
    /// Does not retry and does not verify an earlier <c>act</c>. Fails with <c>POLICY_DENIED</c> on a secure field.
    /// </summary>
    public async Task<string?> TextContentAsync(CancellationToken cancellationToken = default)
    {
        var node = await ResolveReadableAsync(cancellationToken).ConfigureAwait(false);
        var text = node.Text ?? node.Name;
        return text is null ? null : TextRules.Normalize(text);
    }

    /// <summary>
    /// Reads an input value once. Does not retry and does not verify an earlier <c>act</c>. A checkbox or
    /// radio reads its value attribute, <c>on</c> when it has none, whatever its checked state. Fails with
    /// <c>POLICY_DENIED</c> on a secure field.
    /// </summary>
    public async Task<string?> InputValueAsync(CancellationToken cancellationToken = default)
    {
        var node = await ResolveReadableAsync(cancellationToken).ConfigureAwait(false);
        return node.Value;
    }

    /// <summary>Reads one platform attribute of exactly one match once, or null when the node does not have it. Fails with <c>POLICY_DENIED</c> on a secure field.</summary>
    public async Task<string?> GetAttributeAsync(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var node = await ResolveReadableAsync(cancellationToken).ConfigureAwait(false);
        return node.AttributeOf(name);
    }

    /// <summary>Reads current visibility: true when exactly one visible node matches. More than one match fails.</summary>
    public async Task<bool> IsVisibleAsync(CancellationToken cancellationToken = default)
    {
        return await ResolveSingleAsync(_screen.Token(cancellationToken)).ConfigureAwait(false) is { States.Hidden: false };
    }

    /// <summary>Reads current hidden or absent state, the negation of <see cref="IsVisibleAsync"/>.</summary>
    public async Task<bool> IsHiddenAsync(CancellationToken cancellationToken = default)
    {
        return !await IsVisibleAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the enabled state of exactly one match once.</summary>
    public async Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        var node = await ResolveStrictAsync(cancellationToken).ConfigureAwait(false);
        return !node.States.Disabled;
    }

    /// <summary>Reads the disabled state of exactly one match once, the negation of <see cref="IsEnabledAsync"/>.</summary>
    public async Task<bool> IsDisabledAsync(CancellationToken cancellationToken = default)
    {
        return !await IsEnabledAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the checked state of exactly one match once.</summary>
    public async Task<bool> IsCheckedAsync(CancellationToken cancellationToken = default)
    {
        var node = await ResolveStrictAsync(cancellationToken).ConfigureAwait(false);
        return node.States.Checked;
    }

    /// <summary>
    /// Reads the viewport-relative box of exactly one match once, or null when the node lays out no box
    /// (<c>display: none</c> on it or an ancestor, or any <c>display: contents</c> element) or the engine does not report one.
    /// </summary>
    public async Task<BoundingBox?> BoundingBoxAsync(CancellationToken cancellationToken = default)
    {
        var node = await ResolveStrictAsync(cancellationToken).ConfigureAwait(false);
        return node.Rect;
    }

    /// <summary>Counts current matches without waiting.</summary>
    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        var matches = await ResolveAsync(_screen.Token(cancellationToken)).ConfigureAwait(false);
        return matches.Count;
    }

    /// <summary>One <see cref="Nth"/> locator per current match, without waiting. Empty when nothing matches.</summary>
    public async Task<IReadOnlyList<Locator>> AllAsync(CancellationToken cancellationToken = default)
    {
        var count = await CountAsync(cancellationToken).ConfigureAwait(false);
        var locators = new List<Locator>(count);
        for (var i = 0; i < count; i++)
        {
            locators.Add(Nth(i));
        }

        return locators;
    }

    /// <summary>
    /// Reads the whitespace-normalized rendered text of every current match, without waiting. Empty when nothing matches.
    /// Fails with <c>POLICY_DENIED</c> when any match is a secure field.
    /// </summary>
    public async Task<IReadOnlyList<string>> AllTextContentsAsync(CancellationToken cancellationToken = default)
    {
        var matches = await ResolveAsync(_screen.Token(cancellationToken)).ConfigureAwait(false);
        DenySecureRead(matches);
        return matches.Select(node => TextRules.Normalize(node.Text ?? node.Name ?? "")).ToList();
    }

    /// <summary>
    /// Waits for <see cref="LocatorWaitForOptions.State"/>, <see cref="WaitForState.Visible"/> by default:
    /// <see cref="WaitForState.Attached"/> for one match, visible or not;
    /// <see cref="WaitForState.Detached"/> for none; <see cref="WaitForState.Hidden"/> for none
    /// or a hidden one. The timeout defaults to the action timeout. More than one match fails at once.
    /// Does not verify an earlier <c>act</c>.
    /// </summary>
    public async Task WaitForAsync(LocatorWaitForOptions? options = null, CancellationToken cancellationToken = default)
    {
        var state = options?.State ?? WaitForState.Visible;
        var token = _screen.Token(cancellationToken);
        var deadline = DateTime.UtcNow + TimeoutOf(options?.Timeout);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var observation = await _screen.ObserveAsync(token).ConfigureAwait(false);
            var matches = LocatorResolver.Resolve(observation, Query, includeHidden: true);
            if (matches.Count > 1)
            {
                throw Ambiguous(matches.Count);
            }

            var done = state switch
            {
                WaitForState.Attached => matches.Count == 1,
                WaitForState.Visible => matches.Count == 1 && !matches[0].States.Hidden,
                WaitForState.Hidden => matches.Count == 0 || matches[0].States.Hidden,
                _ => matches.Count == 0,
            };
            if (done)
            {
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw new TestException(
                    "TIMEOUT",
                    Query.Describe() + ".waitFor(" + StateName(state) + ") timed out: matched " + matches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " nodes.");
            }

            await Task.Delay(_screen.PollInterval, token).ConfigureAwait(false);
        }
    }

    internal async Task<IReadOnlyList<SemanticNode>> ResolveAsync(CancellationToken cancellationToken, bool includeHidden = false)
    {
        var observation = await _screen.ObserveAsync(cancellationToken).ConfigureAwait(false);
        return LocatorResolver.Resolve(observation, Query, includeHidden);
    }

    internal Screen Screen => _screen;

    private Locator Scope(Locator query) => new(_screen, query.Query with { Parent = Query });

    /// <summary>
    /// Waits up to the action timeout for exactly one enabled match, then acts on it.
    /// More than one match fails at once.
    /// </summary>
    private async Task ActAsync(LocatorAction action, ActionOptions? options, CancellationToken cancellationToken)
    {
        var token = _screen.Token(cancellationToken);
        var deadline = DateTime.UtcNow + TimeoutOf(options?.Timeout);
        while (true)
        {
            var node = await ResolveSingleAsync(token).ConfigureAwait(false);
            if (node is { States.Disabled: false, States.Hidden: false })
            {
                await _screen.PerformAsync(node, action, token).ConfigureAwait(false);
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                throw node is null
                    ? NotFound()
                    : new TestException("NOT_ACTIONABLE", Query.Describe() + (node.States.Hidden ? " is hidden." : " is disabled."));
            }

            await Task.Delay(_screen.PollInterval, token).ConfigureAwait(false);
        }
    }

    /// <summary>A secure field's text, value, and attributes are withheld, so reading them is denied rather than read as empty.</summary>
    internal void DenySecureRead(IReadOnlyList<SemanticNode> nodes)
    {
        if (nodes.Any(node => node.States.Secure))
        {
            throw new TestException("POLICY_DENIED", "reading values from a secure field is denied: " + Query.Describe());
        }
    }

    /// <summary>Returns the one match for a text or value read. A secure match fails with <c>POLICY_DENIED</c>.</summary>
    private async Task<SemanticNode> ResolveReadableAsync(CancellationToken cancellationToken)
    {
        var node = await ResolveStrictAsync(cancellationToken).ConfigureAwait(false);
        DenySecureRead([node]);
        return node;
    }

    private async Task<SemanticNode> ResolveStrictAsync(CancellationToken cancellationToken)
    {
        var node = await ResolveSingleAsync(_screen.Token(cancellationToken)).ConfigureAwait(false);
        return node ?? throw NotFound();
    }

    /// <summary>Returns the one match, or null when nothing matches. More than one match fails.</summary>
    private async Task<SemanticNode?> ResolveSingleAsync(CancellationToken token)
    {
        var matches = await ResolveAsync(token).ConfigureAwait(false);
        if (matches.Count > 1)
        {
            throw Ambiguous(matches.Count);
        }

        return matches.Count == 1 ? matches[0] : null;
    }

    private TimeSpan TimeoutOf(TimeSpan? timeout)
    {
        if (timeout is TimeSpan chosen)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(chosen, TimeSpan.Zero, "timeout");
            return chosen;
        }

        return _screen.ActionTimeout;
    }

    private static string StateName(WaitForState state) => state switch
    {
        WaitForState.Attached => "attached",
        WaitForState.Detached => "detached",
        WaitForState.Hidden => "hidden",
        _ => "visible",
    };

    private TestException Ambiguous(int count) => new("STRICT_MODE", Query.Describe() + " matched " + count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " nodes.");

    private TestException NotFound() => new("NOT_FOUND", Query.Describe() + " matched 0 nodes.");
}

/// <summary>Options for one locator action.</summary>
public class ActionOptions
{
    /// <summary>How long to wait for exactly one enabled match. Defaults to the configured action timeout.</summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>Options for <see cref="Locator.PressSequentiallyAsync"/>.</summary>
public sealed class PressSequentiallyOptions : ActionOptions
{
    /// <summary>How long to wait between characters.</summary>
    public TimeSpan? Delay { get; init; }
}

/// <summary>The state <see cref="Locator.WaitForAsync"/> waits for.</summary>
public enum WaitForState
{
    /// <summary>Exactly one visible match.</summary>
    Visible,

    /// <summary>Exactly one match, visible or not.</summary>
    Attached,

    /// <summary>No match, visible or not.</summary>
    Detached,

    /// <summary>No visible match.</summary>
    Hidden,
}

/// <summary>Options for <see cref="Locator.WaitForAsync"/>.</summary>
public sealed class LocatorWaitForOptions
{
    public WaitForState State { get; init; } = WaitForState.Visible;

    /// <summary>How long to wait. Defaults to the configured action timeout.</summary>
    public TimeSpan? Timeout { get; init; }
}

/// <summary>
/// A locator expression. Query kinds (<c>role</c>, <c>text</c>, <c>label</c>,
/// <c>testid</c>, <c>placeholder</c>, <c>displayValue</c>) search the
/// descendants of <see cref="Parent"/> when it is set. <c>filter</c> and
/// <c>index</c> narrow <see cref="Source"/>.
/// </summary>
internal sealed record LocatorQuery
{
    public const int LastIndex = -1;

    public LocatorQuery? Parent { get; init; }

    public LocatorQuery? Source { get; init; }

    public string Kind { get; init; } = "role";

    public string? Role { get; init; }

    /// <summary>The role query's name, or the value every other query kind matches.</summary>
    public TextMatch? Value { get; init; }

    public bool Exact { get; init; } = true;

    public bool Visible { get; init; }

    public bool? Checked { get; init; }

    public bool? Disabled { get; init; }

    public bool? Selected { get; init; }

    public bool? Expanded { get; init; }

    public bool? Pressed { get; init; }

    public int? Level { get; init; }

    public TextMatch? HasText { get; init; }

    public LocatorQuery? Has { get; init; }

    public int? Index { get; init; }

    public static LocatorQuery ForRole(string role, TextMatch? name, RoleOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        if (options.Level is int level && (level < 1 || level > 6))
        {
            throw new ArgumentOutOfRangeException(nameof(options), level, "Level must be 1 through 6.");
        }

        return new LocatorQuery
        {
            Kind = "role",
            Role = string.Equals(role, "img", StringComparison.OrdinalIgnoreCase) ? "image" : role,
            Value = name,
            Exact = options.Exact,
            Visible = options.Visible,
            Checked = options.Checked,
            Disabled = options.Disabled,
            Selected = options.Selected,
            Expanded = options.Expanded,
            Pressed = options.Pressed,
            Level = options.Level,
        };
    }

    public static LocatorQuery For(string kind, TextMatch value, TextMatchOptions options, string paramName)
    {
        ArgumentNullException.ThrowIfNull(value, paramName);
        ArgumentNullException.ThrowIfNull(options);
        value.ThrowIfBlank(paramName);
        return new LocatorQuery { Kind = kind, Value = value, Exact = options.Exact, Visible = options.Visible };
    }

    public string Describe()
    {
        switch (Kind)
        {
            case "filter":
                return Source!.Describe() + (HasText is not null
                    ? ".filter(hasText: " + HasText + ")"
                    : ".filter(has: " + Has!.Describe() + ")");
            case "index":
                return Source!.Describe() + (Index == LastIndex
                    ? ".last()"
                    : ".nth(" + Index!.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
        }

        var visible = Visible ? ", visible: true" : "";
        var core = Kind switch
        {
            "role" => "getByRole(\"" + Role + "\"" + (Value is null ? "" : ", " + Value) + RoleOptions() + visible + ")",
            "text" => "getByText(" + Value + visible + ")",
            "label" => "getByLabel(" + Value + visible + ")",
            "testid" => "getByTestId(" + Value + visible + ")",
            "placeholder" => "getByPlaceholder(" + Value + visible + ")",
            "displayValue" => "getByDisplayValue(" + Value + visible + ")",
            _ => Kind,
        };
        return Parent is null ? core : Parent.Describe() + "." + core;
    }

    private string RoleOptions()
    {
        var parts = new List<string>();
        void Add(string key, bool? state)
        {
            if (state is bool value)
            {
                parts.Add(key + ": " + (value ? "true" : "false"));
            }
        }

        Add("checked", Checked);
        Add("disabled", Disabled);
        Add("selected", Selected);
        Add("expanded", Expanded);
        Add("pressed", Pressed);
        if (Level is int level)
        {
            parts.Add("level: " + level.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return parts.Count == 0 ? "" : ", { " + string.Join(", ", parts) + " }";
    }
}

/// <summary>
/// The upstream reference locator semantics over a semantic tree. Role queries
/// never match a hidden node unless asked to; the other kinds keep hidden nodes
/// unless the query says <c>visible</c>, which always wins. Text and label queries answer with the innermost
/// match. A scope searches strict descendants of its matches.
/// </summary>
internal static class LocatorResolver
{
    /// <summary>
    /// Returns the nodes <paramref name="query"/> names. <paramref name="includeHidden"/>
    /// asks for every attached node, so role queries match hidden nodes too.
    /// A <c>visible</c> query never matches a hidden node.
    /// </summary>
    public static IReadOnlyList<SemanticNode> Resolve(Observation observation, LocatorQuery query, bool includeHidden = false)
    {
        var tree = new TreeIndex(observation.Roots);
        return Resolve(query, tree.All, tree, includeHidden);
    }

    public static IEnumerable<SemanticNode> Walk(IEnumerable<SemanticNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Walk(root.Children))
            {
                yield return child;
            }
        }
    }

    private static List<SemanticNode> Resolve(LocatorQuery query, IReadOnlyList<SemanticNode> candidates, TreeIndex tree, bool includeHidden)
    {
        switch (query.Kind)
        {
            case "filter":
            {
                if (query.Has is not null && UsesDisplayValue(query.Has))
                {
                    throw UnsupportedDisplayValue();
                }

                var source = Resolve(query.Source!, candidates, tree, includeHidden);
                var includeValue = !UsesDisplayValue(query.Source!);
                return source.Where(node =>
                {
                    if (query.HasText is not null && !SubtreeHasText(node, query.HasText, includeValue))
                    {
                        return false;
                    }

                    return query.Has is null || Resolve(query.Has, tree.DescendantsOf([node], candidates), tree, includeHidden).Count > 0;
                }).ToList();
            }

            case "index":
            {
                var source = Resolve(query.Source!, candidates, tree, includeHidden);
                var position = query.Index == LocatorQuery.LastIndex ? source.Count - 1 : query.Index!.Value;
                return position >= 0 && position < source.Count ? [source[position]] : [];
            }

            default:
            {
                if (query.Parent is not null && UsesDisplayValue(query.Parent))
                {
                    throw UnsupportedDisplayValue();
                }

                var pool = query.Parent is null
                    ? candidates
                    : tree.DescendantsOf(Resolve(query.Parent, candidates, tree, includeHidden), candidates);
                var matches = pool.Where(node => Matches(node, query, includeHidden)).ToList();
                return query.Kind is "text" or "label" ? tree.InnermostOnly(matches) : matches;
            }
        }
    }

    private static bool UsesDisplayValue(LocatorQuery query) =>
        query.Kind == "displayValue"
        || (query.Source is not null && UsesDisplayValue(query.Source))
        || (query.Parent is not null && UsesDisplayValue(query.Parent));

    private static EngineException UnsupportedDisplayValue() =>
        new("UNSUPPORTED_CAPABILITY", "displayValue queries cannot scope child queries or serve as a has-filter in this engine");

    private static bool Matches(SemanticNode node, LocatorQuery query, bool includeHidden)
    {
        if (query.Visible && node.States.Hidden)
        {
            return false;
        }

        var value = query.Value;
        return query.Kind switch
        {
            "role" => RoleMatches(node, query, includeHidden),
            "text" => value!.Matches(node.Name, query.Exact) || value.Matches(node.Text, query.Exact),
            "label" => value!.Matches(node.Name, query.Exact),
            "testid" => value!.Matches(node.TestId, exact: true),
            "placeholder" => value!.Matches(node.Placeholder, query.Exact),
            // A checkbox or radio carries a value it never shows (`on` by default), so it is no candidate.
            "displayValue" => !IsCheckable(node) && value!.Matches(node.Value, query.Exact),
            _ => false,
        };
    }

    // An input whose type is checkbox or radio, as upstream's `input:not([type="checkbox" i]):not([type="radio" i])`.
    internal static bool IsCheckable(SemanticNode node) =>
        node.Attributes.TryGetValue("type", out var type) && (type.Equals("checkbox", StringComparison.OrdinalIgnoreCase) || type.Equals("radio", StringComparison.OrdinalIgnoreCase));

    private static bool RoleMatches(SemanticNode node, LocatorQuery query, bool includeHidden)
    {
        if (node.Role is null || !string.Equals(node.Role, query.Role, StringComparison.OrdinalIgnoreCase) || ((node.States.Hidden || node.States.AriaHidden) && !includeHidden))
        {
            return false;
        }

        if (query.Value is not null && !query.Value.Matches(node.Name, query.Exact))
        {
            return false;
        }

        var states = node.States;
        return StateIs(query.Checked, states.Checked)
            && StateIs(query.Disabled, states.Disabled)
            && StateIs(query.Selected, states.Selected)
            && StateIs(query.Expanded, states.Expanded)
            && StateIs(query.Pressed, states.Pressed)
            && (query.Level is null || node.Level == query.Level);
    }

    private static bool StateIs(bool? wanted, bool actual) => wanted is null || wanted.Value == actual;

    /// <summary>
    /// Whether the node's own name, text, or value, or a descendant's, contains the text, ignoring case.
    /// <paramref name="includeValue"/> false leaves values out, as the text content of a display-value match has none.
    /// </summary>
    private static bool SubtreeHasText(SemanticNode node, TextMatch expected, bool includeValue)
    {
        if (expected.Matches(node.Name, exact: false) || expected.Matches(node.Text, exact: false) || (includeValue && expected.Matches(node.Value, exact: false)))
        {
            return true;
        }

        foreach (var child in node.Children)
        {
            if (SubtreeHasText(child, expected, includeValue))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Every node of an observation in document order, with each node's parent.</summary>
    private sealed class TreeIndex
    {
        private readonly Dictionary<SemanticNode, SemanticNode> _parents = new(ReferenceEqualityComparer.Instance);

        public TreeIndex(IEnumerable<SemanticNode> roots)
        {
            foreach (var root in roots)
            {
                Add(root, null);
            }
        }

        public List<SemanticNode> All { get; } = [];

        /// <summary>The candidates that are strict descendants of any of <paramref name="ancestors"/>, in document order.</summary>
        public List<SemanticNode> DescendantsOf(IReadOnlyCollection<SemanticNode> ancestors, IReadOnlyList<SemanticNode> candidates)
        {
            if (ancestors.Count == 0)
            {
                return [];
            }

            var set = new HashSet<SemanticNode>(ancestors, ReferenceEqualityComparer.Instance);
            return candidates.Where(node => Ancestors(node).Any(set.Contains)).ToList();
        }

        /// <summary>
        /// Drops every match that contains another match. A container that
        /// repeats its child's text is not a second match.
        /// </summary>
        public List<SemanticNode> InnermostOnly(List<SemanticNode> matches)
        {
            var containers = new HashSet<SemanticNode>(ReferenceEqualityComparer.Instance);
            foreach (var match in matches)
            {
                containers.UnionWith(Ancestors(match));
            }

            return matches.Where(node => !containers.Contains(node)).ToList();
        }

        private IEnumerable<SemanticNode> Ancestors(SemanticNode node)
        {
            for (var current = node; _parents.TryGetValue(current, out var parent); current = parent)
            {
                yield return parent;
            }
        }

        private void Add(SemanticNode node, SemanticNode? parent)
        {
            All.Add(node);
            if (parent is not null)
            {
                _parents[node] = parent;
            }

            foreach (var child in node.Children.Concat(node.InlineNodes))
            {
                Add(child, node);
            }
        }
    }
}
