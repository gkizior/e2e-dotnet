// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.Concurrent;
using Microsoft.Extensions.Time.Testing;

namespace E2E.Tests.AgentObservation;

/// <summary>
/// <c>settleObservation</c>: the loop that waits for the screen to leave the pre-action shape, then to hold still.
/// Upstream's "settles a changed canvas from matching permitted screenshots instead of waiting out both windows" is not
/// ported: the port's observations carry no pixels.
/// </summary>
public sealed class SettleObservationTests
{
    private static readonly TimeSpan FastPoll = TimeSpan.FromMilliseconds(5);

    private static readonly TimeSpan FastStableWait = TimeSpan.FromMilliseconds(30);

    /// <summary>The pre-action shape and the window to leave it in.</summary>
    private static PendingChange Leaving(TimeProvider time) => new("old", time.GetUtcNow() + TimeSpan.FromMilliseconds(150));

    [Fact]
    public async Task Waits_for_the_screen_to_leave_the_pre_action_shape_before_settling_on_it()
    {
        var source = new Scripted("old", "old", "old", "new", "new", "new");
        var value = await SettleAsync(source, TimeProvider.System, Fast(TimeProvider.System, changedFrom: true));
        Assert.Equal("new", value);
    }

    [Fact]
    public async Task Returns_the_unchanged_screen_once_the_change_wait_runs_out()
    {
        var time = new SteppingTime();
        var source = new Scripted("old");
        var started = time.GetUtcNow();
        var value = await RunAllTimersAsync(time, SettleAsync(source, time, Fast(time, changedFrom: true)));
        Assert.Equal("old", value);
        Assert.True(time.GetUtcNow() - started >= TimeSpan.FromMilliseconds(150));
    }

    [Fact]
    public async Task Never_settles_on_a_transitional_capture_while_the_change_wait_lasts()
    {
        var source = new Scripted("old", "", "", "new", "new");
        var options = new SettleOptions<string>
        {
            ChangedFrom = Leaving(TimeProvider.System),
            Transitional = value => value.Length == 0,
            Poll = FastPoll,
            StableWait = FastStableWait,
        };
        Assert.Equal("new", await SettleAsync(source, TimeProvider.System, options));
    }

    [Fact]
    public async Task Settles_on_stability_alone_without_a_pre_action_shape()
    {
        var source = new Scripted("a", "b", "b");
        Assert.Equal("b", await SettleAsync(source, TimeProvider.System, Fast(TimeProvider.System, changedFrom: false)));
        Assert.Equal(3, source.Calls);
    }

    [Fact]
    public async Task Accepts_a_stable_empty_screen_when_no_action_is_pending()
    {
        var source = new Scripted("", "", "new");
        var options = new SettleOptions<string> { Transitional = value => value.Length == 0, Poll = FastPoll, StableWait = FastStableWait };
        Assert.Equal("", await SettleAsync(source, TimeProvider.System, options));
        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public async Task Counts_a_slow_first_capture_toward_the_change_window_then_still_checks_stability()
    {
        var time = new SteppingTime();
        var started = time.GetUtcNow();
        var captures = 0;
        async Task<string> Capture(CancellationToken token)
        {
            captures++;
            await Task.Delay(TimeSpan.FromMilliseconds(400), time, token);
            return "old";
        }

        var pending = global::E2E.AgentObservation.SettleAsync(
            Capture,
            value => value,
            new SettleOptions<string>
            {
                ChangedFrom = new PendingChange("old", started + TimeSpan.FromMilliseconds(500)),
                StableWait = TimeSpan.FromSeconds(1),
                Poll = TimeSpan.FromMilliseconds(100),
            },
            time,
            CancellationToken.None);
        Assert.Equal("old", await RunAllTimersAsync(time, pending));
        Assert.Equal(TimeSpan.FromMilliseconds(1_400), time.GetUtcNow() - started);
        Assert.Equal(3, captures);
    }

    [Fact]
    public async Task Keeps_a_fresh_stability_check_when_the_action_window_expired_before_observation()
    {
        var time = new SteppingTime();
        var started = time.GetUtcNow();
        var source = new Scripted("old", "new", "new");
        var options = new SettleOptions<string>
        {
            ChangedFrom = new PendingChange("old", started - TimeSpan.FromMilliseconds(1)),
            StableWait = TimeSpan.FromSeconds(1),
            Poll = TimeSpan.FromMilliseconds(100),
        };
        Assert.Equal("new", await RunAllTimersAsync(time, SettleAsync(source, time, options)));
        Assert.Equal(TimeSpan.FromMilliseconds(200), time.GetUtcNow() - started);
        Assert.Equal(3, source.Calls);
    }

    [Fact]
    public async Task Does_not_treat_matching_empty_navigation_captures_as_stable_after_the_action_deadline()
    {
        var time = new SteppingTime();
        var started = time.GetUtcNow();
        var source = new Scripted("", "", "new", "new");
        var options = new SettleOptions<string>
        {
            ChangedFrom = new PendingChange("old", started - TimeSpan.FromMilliseconds(1)),
            Transitional = value => value.Length == 0,
            StableWait = TimeSpan.FromSeconds(1),
            Poll = TimeSpan.FromMilliseconds(100),
        };
        Assert.Equal("new", await RunAllTimersAsync(time, SettleAsync(source, time, options)));
        Assert.Equal(TimeSpan.FromMilliseconds(300), time.GetUtcNow() - started);
        Assert.Equal(4, source.Calls);
    }

    [Fact]
    public async Task Bounds_the_stability_check_when_navigation_stays_empty_after_the_action_deadline()
    {
        var time = new SteppingTime();
        var started = time.GetUtcNow();
        var source = new Scripted("");
        var options = new SettleOptions<string>
        {
            ChangedFrom = new PendingChange("old", started - TimeSpan.FromMilliseconds(1)),
            Transitional = value => value.Length == 0,
            StableWait = TimeSpan.FromSeconds(1),
            Poll = TimeSpan.FromMilliseconds(100),
        };
        Assert.Equal("", await RunAllTimersAsync(time, SettleAsync(source, time, options)));
        Assert.Equal(TimeSpan.FromSeconds(1), time.GetUtcNow() - started);
    }

    private static SettleOptions<string> Fast(TimeProvider time, bool changedFrom) => new()
    {
        ChangedFrom = changedFrom ? Leaving(time) : null,
        Poll = FastPoll,
        StableWait = FastStableWait,
    };

    private static Task<string> SettleAsync(Scripted source, TimeProvider time, SettleOptions<string> options)
    {
        return global::E2E.AgentObservation.SettleAsync(source.CaptureAsync, value => value, options, time, CancellationToken.None);
    }

    /// <summary>
    /// Runs every timer the settle starts until it returns: each time the settle waits on the fake clock, the clock
    /// moves to that timer's due time.
    /// </summary>
    private static async Task<T> RunAllTimersAsync<T>(SteppingTime time, Task<T> pending)
    {
        while (true)
        {
            var due = TimeSpan.Zero;
            while (!pending.IsCompleted && !time.Due.TryDequeue(out due))
            {
                await Task.Delay(1);
            }

            if (pending.IsCompleted)
            {
                return await pending;
            }

            time.Advance(due);
        }
    }

    /// <summary>A fake clock that tells which timer was started last.</summary>
    private sealed class SteppingTime : FakeTimeProvider
    {
        public ConcurrentQueue<TimeSpan> Due { get; } = new();

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            Due.Enqueue(dueTime);
            return base.CreateTimer(callback, state, dueTime, period);
        }
    }

    /// <summary>Captures the scripted values in order, then the last one forever.</summary>
    private sealed class Scripted(params string[] values)
    {
        public int Calls { get; private set; }

        public Task<string> CaptureAsync(CancellationToken token) => Task.FromResult(values[Math.Min(Calls++, values.Length - 1)]);
    }
}
