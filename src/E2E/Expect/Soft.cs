// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace E2E;

/// <summary>
/// The locator matchers of <see cref="LocatorExpect"/>, but an
/// <c>ASSERTION_FAILED</c> is kept on the session instead of thrown, and the
/// test body runs on. Any other error, such as <c>STRICT_MODE</c>, still throws.
/// </summary>
public sealed class SoftLocatorExpect
{
    private readonly LocatorExpect _expect;
    private readonly SoftFailures _failures;

    internal SoftLocatorExpect(LocatorExpect expect, SoftFailures failures)
    {
        _expect = expect;
        _failures = failures;
    }

    /// <summary>Inverts the matcher, as <see cref="LocatorExpect.Not"/>. A failure is still kept, not thrown.</summary>
    public SoftLocatorExpect Not => new(_expect.Not, _failures);

    public Task ToBeVisibleAsync(bool visible = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToBeVisibleAsync(visible, timeout, cancellationToken));

    public Task ToBeHiddenAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToBeHiddenAsync(timeout, cancellationToken));

    public Task ToBeAttachedAsync(bool attached = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToBeAttachedAsync(attached, timeout, cancellationToken));

    public Task ToBeEnabledAsync(bool enabled = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToBeEnabledAsync(enabled, timeout, cancellationToken));

    public Task ToBeDisabledAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToBeDisabledAsync(timeout, cancellationToken));

    public Task ToBeCheckedAsync(bool isChecked = true, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToBeCheckedAsync(isChecked, timeout, cancellationToken));

    public Task ToBeSelectedAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToBeSelectedAsync(timeout, cancellationToken));

    public Task ToBeExpandedAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToBeExpandedAsync(timeout, cancellationToken));

    public Task ToBeFocusedAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToBeFocusedAsync(timeout, cancellationToken));

    public Task ToHaveTextAsync(TextMatch expected, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToHaveTextAsync(expected, ignoreCase, timeout, cancellationToken));

    public Task ToHaveTextAsync(IReadOnlyList<TextMatch> expected, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToHaveTextAsync(expected, ignoreCase, timeout, cancellationToken));

    public Task ToContainTextAsync(TextMatch expected, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToContainTextAsync(expected, ignoreCase, timeout, cancellationToken));

    public Task ToContainTextAsync(IReadOnlyList<TextMatch> expected, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToContainTextAsync(expected, ignoreCase, timeout, cancellationToken));

    public Task ToHaveValueAsync(TextMatch expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToHaveValueAsync(expected, timeout, cancellationToken));

    public Task ToHaveAccessibleNameAsync(TextMatch expected, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToHaveAccessibleNameAsync(expected, ignoreCase, timeout, cancellationToken));

    public Task ToHaveAttributeAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToHaveAttributeAsync(name, timeout, cancellationToken));

    public Task ToHaveAttributeAsync(string name, TextMatch value, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToHaveAttributeAsync(name, value, ignoreCase, timeout, cancellationToken));

    public Task ToHaveCountAsync(int count, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => KeepAsync(_expect.ToHaveCountAsync(count, timeout, cancellationToken));

    private async Task KeepAsync(Task matcher)
    {
        try
        {
            await matcher.ConfigureAwait(false);
        }
        catch (TestException ex) when (ex.Code == "ASSERTION_FAILED" && _failures.Keep(ex))
        {
        }
    }
}

/// <summary>
/// The soft failures of one session. Each kept failure goes to the handler
/// when there is one, otherwise it waits for <see cref="Close"/>. Once closed,
/// nothing is kept and a soft matcher throws as a hard one would.
/// </summary>
internal sealed class SoftFailures
{
    private readonly List<TestException> _failures = [];
    private readonly Action<TestException>? _handler;
    private readonly Lock _gate = new();
    private bool _open = true;

    public SoftFailures(Action<TestException>? handler = null)
    {
        _handler = handler;
    }

    /// <summary>Whether a failure is kept and not yet closed. Failures sent to a handler are not kept.</summary>
    public bool Any
    {
        get
        {
            lock (_gate)
            {
                return _failures.Count > 0;
            }
        }
    }

    public bool Keep(TestException error)
    {
        lock (_gate)
        {
            if (!_open)
            {
                return false;
            }

            if (_handler is null)
            {
                _failures.Add(error);
                return true;
            }
        }

        _handler(error);
        return true;
    }

    /// <summary>Ends collection and returns one <c>ASSERTION_FAILED</c> for everything kept, or null.</summary>
    public TestException? Close()
    {
        TestException[] failures;
        lock (_gate)
        {
            _open = false;
            failures = [.. _failures];
            _failures.Clear();
        }

        if (failures.Length == 0)
        {
            return null;
        }

        var lines = new List<string>
        {
            failures.Length.ToString(CultureInfo.InvariantCulture) + " soft assertion" + (failures.Length == 1 ? "" : "s") + " failed",
        };
        for (var i = 0; i < failures.Length; i++)
        {
            lines.Add((i + 1).ToString(CultureInfo.InvariantCulture) + ". " + failures[i].Message.Replace("\n", "\n   ", StringComparison.Ordinal));
        }

        return new TestException("ASSERTION_FAILED", string.Join('\n', lines), failures[0]);
    }
}
