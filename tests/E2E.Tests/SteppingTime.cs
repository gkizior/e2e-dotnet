// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Time.Testing;

namespace E2E.Tests;

/// <summary>
/// A fake clock that knows its pending timers, so a test can run a poll to its end without waiting:
/// <see cref="RunAsync"/> moves the clock to the next timer that is still pending once the poll has gone idle.
/// </summary>
internal sealed class SteppingTime : FakeTimeProvider
{
    private readonly List<Pending> _pending = [];

    public TimeSpan Elapsed => GetUtcNow() - Start;

    /// <summary>Whether some timer is waiting to fire.</summary>
    public bool HasTimer => NextDue() is not null;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        var pending = new Pending(base.CreateTimer(callback, state, dueTime, period), GetUtcNow() + dueTime, this);
        lock (_pending)
        {
            _pending.Add(pending);
        }

        return pending;
    }

    /// <summary>Runs the clock until <paramref name="pending"/> ends, then returns its outcome.</summary>
    public async Task RunAsync(Task pending)
    {
        while (!pending.IsCompleted)
        {
            // Let the poll finish what it is doing, such as a read that completes and drops its own timeout.
            for (var round = 0; round < 3; round++)
            {
                await Task.Delay(1);
                await Task.Run(() => { });
            }

            if (!pending.IsCompleted && NextDue() is { } due)
            {
                Advance(due > GetUtcNow() ? due - GetUtcNow() : TimeSpan.Zero);
            }
        }

        await pending;
    }

    private DateTimeOffset? NextDue()
    {
        lock (_pending)
        {
            _pending.RemoveAll(timer => timer.Disposed || timer.Due <= GetUtcNow());
            return _pending.Count == 0 ? null : _pending.Min(timer => timer.Due);
        }
    }

    private sealed class Pending(ITimer inner, DateTimeOffset due, SteppingTime owner) : ITimer
    {
        public DateTimeOffset Due { get; } = due;

        public bool Disposed { get; private set; }

        public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException("A poll never retimes a timer.");

        public void Dispose()
        {
            lock (owner._pending)
            {
                Disposed = true;
            }

            inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
