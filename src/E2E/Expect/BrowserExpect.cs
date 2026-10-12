// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text.RegularExpressions;
using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>
/// Polling assertions for the <see cref="Browser"/>: its URL and its title. Like the
/// locator matchers, a passing one verifies the previous <c>agent.act</c>. A read is
/// bounded by the matcher's own deadline, so a page that stops answering ends the
/// assertion at its timeout.
/// </summary>
public sealed class BrowserExpect
{
    /// <summary>How long a negated matcher's condition must stay false before it passes, as <see cref="LocatorExpect"/>.</summary>
    private static readonly TimeSpan NegationGrace = LocatorExpect.NegationGrace;

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    private readonly Browser _browser;
    private readonly bool _negated;

    internal BrowserExpect(Browser browser, bool negated)
    {
        _browser = browser;
        _negated = negated;
    }

    /// <summary>Inverts the matcher. A negated matcher passes after its condition has been false for 1000 ms in a row.</summary>
    public BrowserExpect Not => new(_browser, !_negated);

    /// <summary>
    /// Waits for the URL to equal <paramref name="expected"/>, resolved against the base URL.
    /// Both sides are compared normalized (default port, trailing slash, dot segments, percent-encoding).
    /// <paramref name="ignoreCase"/> compares without case.
    /// </summary>
    public Task ToHaveURLAsync(string expected, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var target = Routes.Absolute(_browser.BaseUrl, expected).AbsoluteUri;
        var comparison = ignoreCase == true ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return UrlAsync(expected, ignoreCase, timeout, current => string.Equals(Normalize(current), target, comparison), cancellationToken);
    }

    /// <summary>
    /// Waits for the URL to match <paramref name="expected"/>, tested against the complete URL.
    /// <paramref name="ignoreCase"/> true adds <see cref="RegexOptions.IgnoreCase"/> to the pattern, and false removes it.
    /// </summary>
    public Task ToHaveURLAsync(Regex expected, bool? ignoreCase = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        var pattern = ignoreCase switch
        {
            true => new Regex(expected.ToString(), expected.Options | RegexOptions.IgnoreCase, expected.MatchTimeout),
            false => new Regex(expected.ToString(), expected.Options & ~RegexOptions.IgnoreCase, expected.MatchTimeout),
            null => expected,
        };
        return UrlAsync(TextMatch.FromRegex(expected).ToString(), ignoreCase, timeout, pattern.IsMatch, cancellationToken);
    }

    /// <summary>Waits for the title to equal <paramref name="expected"/> after whitespace normalization, or to match it.</summary>
    public Task ToHaveTitleAsync(TextMatch expected, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        return PollAsync(
            "toHaveTitle",
            "title " + expected,
            timeout,
            async token =>
            {
                var title = await _browser.TitleAsync(token).ConfigureAwait(false);
                return (TextRules.Compare(title, expected, contains: false, normalize: true, ignoreCase: null), "title " + LocatorExpect.Quote(title));
            },
            cancellationToken);
    }

    // The URL is compared as the browser serializes it, so the expected side goes through the same Uri form as the read one.
    private static string Normalize(string url) => Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.AbsoluteUri : url;

    private Task UrlAsync(string label, bool? ignoreCase, TimeSpan? timeout, Func<string, bool> matches, CancellationToken cancellationToken)
    {
        return PollAsync(
            "toHaveURL",
            "URL " + label + (ignoreCase == true ? " (ignoring case)" : ""),
            timeout,
            async token =>
            {
                var url = await _browser.UrlAsync(token).ConfigureAwait(false);
                return (matches(url), "URL " + url);
            },
            cancellationToken);
    }

    /// <summary>
    /// Polls until the condition holds, or, negated, until it has been false for <see cref="NegationGrace"/>
    /// in a row (or for the whole budget when that is shorter), as the locator matchers do. A read the
    /// deadline cut off after an earlier one completed ends the poll on the last sample.
    /// </summary>
    private async Task PollAsync(string matcher, string describeExpected, TimeSpan? timeout, Func<CancellationToken, Task<(bool Holds, string Observed)>> read, CancellationToken cancellationToken)
    {
        if (timeout is { } given)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(given, TimeSpan.Zero, nameof(timeout));
        }

        var token = _browser.Token(cancellationToken);
        var budget = timeout ?? _browser.AssertionTimeout;
        var grace = budget < NegationGrace ? budget : NegationGrace;
        var clock = Stopwatch.StartNew();
        var readAt = TimeSpan.Zero;
        TimeSpan? falseSince = null;
        string? observed = null;
        bool Holds(TimeSpan now) => falseSince is { } since && now - since >= grace;
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                var left = budget - readAt;
                using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
                bounded.CancelAfter(left > PollInterval ? left : PollInterval);
                (bool Holds, string Observed) sample;
                try
                {
                    sample = await read(bounded.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (bounded.IsCancellationRequested && !token.IsCancellationRequested && ex is OperationCanceledException or EngineException { Code: EngineErrorCodes.Cancelled or EngineErrorCodes.OperationTimeout })
                {
                    if (observed is null || left >= PollInterval)
                    {
                        throw new EngineException(EngineErrorCodes.OperationTimeout, "expect." + matcher + " read timed out", retryable: false, ex);
                    }

                    if (_negated && Holds(clock.Elapsed < budget ? clock.Elapsed : budget))
                    {
                        _browser.NotifyVerified();
                        return;
                    }

                    throw Failure(matcher, describeExpected, observed, ex);
                }

                observed = sample.Observed;
                if (!_negated && sample.Holds)
                {
                    _browser.NotifyVerified();
                    return;
                }

                falseSince = _negated && !sample.Holds ? falseSince ?? readAt : null;
                var now = clock.Elapsed;
                if (Holds(now))
                {
                    _browser.NotifyVerified();
                    return;
                }

                if (now >= budget)
                {
                    throw Failure(matcher, describeExpected, observed);
                }

                // No sleep runs past the deadline, so the last read starts at it and never sees a state that arrived later.
                var remaining = budget - now;
                await Task.Delay(remaining < PollInterval ? remaining : PollInterval, token).ConfigureAwait(false);

                readAt = clock.Elapsed;
                if (_negated && readAt >= budget)
                {
                    if (Holds(readAt))
                    {
                        _browser.NotifyVerified();
                        return;
                    }

                    throw Failure(matcher, describeExpected, observed);
                }
            }
        }
        catch (OperationCanceledException ex) when (token.IsCancellationRequested)
        {
            throw new TestException(EngineErrorCodes.Cancelled, "operation cancelled", ex);
        }
    }

    private TestException Failure(string matcher, string describeExpected, string observed, Exception? cause = null)
    {
        var message = "expect." + (_negated ? "not." : "") + matcher + " failed\nexpected: " + (_negated ? "not " : "") + describeExpected + "\nobserved: " + observed;
        message = _browser.Redact(message);
        return cause is null ? new TestException("ASSERTION_FAILED", message) : new TestException("ASSERTION_FAILED", message, cause);
    }
}
