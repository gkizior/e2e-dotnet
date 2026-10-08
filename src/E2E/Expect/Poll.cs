// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;

namespace E2E;

/// <summary>How <see cref="Expect.Poll{T}(Func{CancellationToken, Task{T}}, PollOptions?)"/> re-reads a value.</summary>
public sealed class PollOptions
{
    /// <summary>Deadline for the matcher to hold. The default is 5 seconds.</summary>
    public TimeSpan? Timeout { get; init; }

    /// <summary>Pause between reads. The default is 100 milliseconds.</summary>
    public TimeSpan? Interval { get; init; }

    /// <summary>Extra line in the timeout error.</summary>
    public string? Message { get; init; }
}

/// <summary>
/// Re-reads a value until the chosen matcher holds or the timeout passes. A
/// read that throws is retried, and its error is reported if the poll times
/// out. With NUnit, <c>ToMatchAsync</c> from <c>E2E.NUnit</c> takes any NUnit constraint.
/// </summary>
public sealed class PollExpectation<T>
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(100);

    private readonly Func<CancellationToken, Task<T>> _read;
    private readonly PollOptions _options;
    private readonly bool _negated;

    internal PollExpectation(Func<CancellationToken, Task<T>> read, PollOptions options, bool negated)
    {
        _read = read;
        _options = options;
        _negated = negated;
    }

    /// <summary>The same matchers, negated.</summary>
    public PollExpectation<T> Not => new(_read, _options, !_negated);

    /// <summary>Passes when the read value equals <paramref name="expected"/> by <see cref="EqualityComparer{T}.Default"/>.</summary>
    public Task ToBeAsync(T expected, CancellationToken cancellationToken = default)
    {
        return PollAsync(
            "toBe",
            value => EqualityComparer<T>.Default.Equals(value, expected),
            value => "received " + Format(value) + ", expected " + (_negated ? "not " : "") + Format(expected),
            cancellationToken);
    }

    /// <summary>Passes when <paramref name="predicate"/> returns true for the read value. <paramref name="description"/> names the condition in the error.</summary>
    public Task ToSatisfyAsync(Func<T, bool> predicate, string? description = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        var condition = string.IsNullOrWhiteSpace(description) ? "the predicate" : description;
        return PollAsync(
            "toSatisfy",
            predicate,
            value => "received " + Format(value) + ", which does " + (_negated ? "" : "not ") + "satisfy " + condition,
            cancellationToken);
    }

    private async Task PollAsync(string matcher, Func<T, bool> check, Func<T, string> describe, CancellationToken cancellationToken)
    {
        var timeout = _options.Timeout ?? DefaultTimeout;
        var interval = _options.Interval ?? DefaultInterval;
        var started = DateTime.UtcNow;
        var deadline = started + timeout;
        string? last = null;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = deadline - DateTime.UtcNow;
            if (remaining < TimeSpan.Zero)
            {
                break;
            }

            T value;
            try
            {
                value = await _read(cancellationToken).WaitAsync(remaining, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (TimeoutException)
            {
                // The read outlived the deadline; its outcome is dropped.
                break;
            }
            catch (Exception ex)
            {
                last = ex.Message;
                await PauseAsync(deadline, interval, cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (check(value) != _negated)
            {
                return;
            }

            last = describe(value);
            await PauseAsync(deadline, interval, cancellationToken).ConfigureAwait(false);
        }

        var lines = new List<string>
        {
            "expect.poll(...)." + (_negated ? "not." : "") + matcher + "(...) timed out after "
                + ((long)timeout.TotalMilliseconds).ToString(CultureInfo.InvariantCulture) + " ms",
        };
        if (_options.Message is not null)
        {
            lines.Add(_options.Message);
        }

        lines.Add("last: " + (last ?? "no read completed"));
        throw new TestException("ASSERTION_FAILED", string.Join('\n', lines));
    }

    private static Task PauseAsync(DateTime deadline, TimeSpan interval, CancellationToken cancellationToken)
    {
        var remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        // Rounded up to a whole millisecond: a delay under one completes at once, and the loop would spin to the deadline.
        return Task.Delay(remaining < interval ? TimeSpan.FromMilliseconds(Math.Ceiling(remaining.TotalMilliseconds)) : interval, cancellationToken);
    }

    private static string Format(T value)
    {
        return value switch
        {
            null => "null",
            string text => "\"" + text + "\"",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
    }

    internal static void Validate(PollOptions options)
    {
        if (options.Timeout is { } timeout && timeout < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), timeout, "expect.poll timeout must be 0 or more.");
        }

        if (options.Interval is { } interval && interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), interval, "expect.poll interval must be above 0.");
        }
    }
}
