// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using E2E.Engine;

namespace E2E;

/// <summary>Polling assertions for a <see cref="Locator"/>. A passing locator assertion verifies the previous <c>agent.act</c>.</summary>
public static class Expect
{
    public static LocatorExpect That(Locator locator)
    {
        ArgumentNullException.ThrowIfNull(locator);
        return new LocatorExpect(locator);
    }
}

/// <summary>Matchers that retry until they pass or the assertion timeout elapses.</summary>
public sealed class LocatorExpect
{
    private readonly Locator _locator;

    internal LocatorExpect(Locator locator)
    {
        _locator = locator;
    }

    public Task ToBeVisibleAsync(CancellationToken cancellationToken = default)
    {
        return PollAsync(
            "toBeVisible",
            matches => matches.Count == 1 ? null : "matched " + matches.Count.ToString(CultureInfo.InvariantCulture) + " nodes",
            strict: true,
            cancellationToken);
    }

    public Task ToBeHiddenAsync(CancellationToken cancellationToken = default)
    {
        return PollAsync(
            "toBeHidden",
            matches => matches.Count == 0 ? null : "matched " + matches.Count.ToString(CultureInfo.InvariantCulture) + " nodes",
            strict: false,
            cancellationToken);
    }

    public Task ToHaveTextAsync(string expected, bool exact = true, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return PollSingleAsync(
            "toHaveText",
            node => TextEquals(ShownText(node), expected, exact) ? null : "had text " + Quote(ShownText(node)),
            cancellationToken);
    }

    public Task ToContainTextAsync(string expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return PollSingleAsync(
            "toContainText",
            node => Internal.TextRules.Contains(ShownText(node), expected) ? null : "had text " + Quote(ShownText(node)),
            cancellationToken);
    }

    public Task ToHaveCountAsync(int count, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return PollAsync(
            "toHaveCount",
            matches => matches.Count == count ? null : "matched " + matches.Count.ToString(CultureInfo.InvariantCulture) + " nodes",
            strict: false,
            cancellationToken);
    }

    public Task ToBeEnabledAsync(CancellationToken cancellationToken = default)
    {
        return PollSingleAsync("toBeEnabled", node => node.States.Disabled ? "was disabled" : null, cancellationToken);
    }

    public Task ToBeDisabledAsync(CancellationToken cancellationToken = default)
    {
        return PollSingleAsync("toBeDisabled", node => node.States.Disabled ? null : "was enabled", cancellationToken);
    }

    public Task ToBeCheckedAsync(CancellationToken cancellationToken = default)
    {
        return PollSingleAsync("toBeChecked", node => node.States.Checked ? null : "was not checked", cancellationToken);
    }

    public Task ToHaveValueAsync(string expected, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return PollSingleAsync(
            "toHaveValue",
            node => string.Equals(node.Value ?? "", expected, StringComparison.Ordinal) ? null : "had value " + Quote(node.Value),
            cancellationToken);
    }

    private Task PollSingleAsync(string matcher, Func<SemanticNode, string?> check, CancellationToken cancellationToken)
    {
        return PollAsync(
            matcher,
            matches =>
            {
                if (matches.Count != 1)
                {
                    return "matched " + matches.Count.ToString(CultureInfo.InvariantCulture) + " nodes";
                }

                return check(matches[0]);
            },
            strict: true,
            cancellationToken);
    }

    private async Task PollAsync(string matcher, Func<IReadOnlyList<SemanticNode>, string?> check, bool strict, CancellationToken cancellationToken)
    {
        var token = _locator.Screen.Token(cancellationToken);
        var timeout = _locator.Screen.AssertionTimeout;
        var deadline = DateTime.UtcNow + timeout;
        string? failure = null;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var matches = await _locator.ResolveAsync(token).ConfigureAwait(false);
            failure = check(matches);
            if (failure is null)
            {
                _locator.Screen.NotifyVerified();
                return;
            }

            if (DateTime.UtcNow >= deadline)
            {
                var code = strict && IsStrict(failure) ? "STRICT_MODE" : "ASSERTION_FAILED";
                throw new TestException(
                    code,
                    "expect(" + _locator.Query.Describe() + ")." + matcher + " failed: " + failure);
            }

            await Task.Delay(_locator.Screen.PollInterval, token).ConfigureAwait(false);
        }
    }

    private static string? ShownText(SemanticNode node) => node.Text ?? node.Name;

    private static bool TextEquals(string? actual, string expected, bool exact)
    {
        return Internal.TextRules.Matches(actual, expected, exact);
    }

    private static string Quote(string? value) => "\"" + (value ?? "") + "\"";

    private static bool IsStrict(string failure)
    {
        const string prefix = "matched ";
        if (!failure.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = failure[prefix.Length..];
        var space = rest.IndexOf(' ');
        if (space <= 0)
        {
            return false;
        }

        return int.TryParse(rest[..space], NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 1;
    }
}
