// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Mobile.Tests.LimrunHeartbeat;

/// <summary>Time the test moves by hand: a delay completes when <see cref="AdvanceAsync"/> reaches it, so minutes of waiting take no time.</summary>
internal sealed class ManualDelay
{
    private readonly List<(long Due, TaskCompletionSource Source)> _waiters = [];
    private long _now;

    public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
    {
        var source = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_waiters)
        {
            _waiters.Add((_now + (long)delay.TotalMilliseconds, source));
        }

        cancellationToken.Register(() =>
        {
            lock (_waiters)
            {
                _waiters.RemoveAll(waiter => waiter.Source == source);
            }

            source.TrySetCanceled(cancellationToken);
        });
        return source.Task;
    }

    /// <summary>
    /// Moves time forward, completing each delay it passes in order. With <paramref name="expectNext"/>, it waits after each
    /// one for the code under test to start its next delay, so every step ends where the caller would see it.
    /// </summary>
    public async Task AdvanceAsync(TimeSpan by, bool expectNext = true)
    {
        // The code under test starts its first delay on its own thread.
        if (expectNext)
        {
            await NextDelayAsync().ConfigureAwait(false);
        }

        long target;
        lock (_waiters)
        {
            target = _now + (long)by.TotalMilliseconds;
        }

        while (true)
        {
            TaskCompletionSource due;
            lock (_waiters)
            {
                var next = _waiters.Where(waiter => waiter.Due <= target).OrderBy(waiter => waiter.Due).Cast<(long Due, TaskCompletionSource Source)?>().FirstOrDefault();
                if (next is null)
                {
                    _now = target;
                    return;
                }

                _now = next.Value.Due;
                _waiters.Remove(next.Value);
                due = next.Value.Source;
            }

            due.TrySetResult();
            if (expectNext)
            {
                await NextDelayAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Waits until a delay is pending, or for a moment: the code under test may have stopped.</summary>
    private async Task NextDelayAsync()
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            lock (_waiters)
            {
                if (_waiters.Count > 0)
                {
                    return;
                }
            }

            await Task.Delay(2).ConfigureAwait(false);
        }
    }
}
