// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.Time;

/// <summary>
/// Upstream tests <c>pollCondition</c> on fake timers. The port polls inside the
/// locator matchers, so these run a matcher over a screen whose every read is
/// scripted, on a fake clock that moves to each timer the poll starts. The port
/// samples every 50 ms, upstream every 100 ms.
/// </summary>
public sealed class PollConditionTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(1000);

    private static readonly SemanticNode Shown = new() { Ref = "s", Role = "status", Name = "Saving" };

    private static readonly SemanticNode Unchecked = new() { Ref = "c", Role = "checkbox", Name = "Terms" };

    private readonly SteppingTime _time = new();

    [Fact]
    public async Task Throws_the_caller_error_at_the_deadline()
    {
        var screen = Scripted(_ => []);
        var error = await Assert.ThrowsAsync<TestException>(() => Drive(Expect.That(screen.GetByRole("status")).ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(350))));
        Assert.Equal("ASSERTION_FAILED", error.Code);
        Assert.InRange(_time.Elapsed, TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(1000));
    }

    [Fact]
    public async Task Takes_its_last_read_at_the_deadline_not_a_poll_tick_past_it()
    {
        // 330 ms is not a multiple of the 50 ms sample: the state arrives just after it.
        var budget = TimeSpan.FromMilliseconds(330);
        var readsAt = new List<double>();
        var screen = Over(_ =>
        {
            readsAt.Add(_time.Elapsed.TotalMilliseconds);
            return Task.FromResult<SemanticNode[]>(_time.Elapsed > budget ? [Shown] : []);
        });
        var error = await Assert.ThrowsAsync<TestException>(() => Drive(Expect.That(screen.GetByRole("status")).ToBeVisibleAsync(timeout: budget)));
        Assert.Equal("ASSERTION_FAILED", error.Code);
        Assert.Equal([0, 50, 100, 150, 200, 250, 300, 330], readsAt);
    }

    [Fact]
    public async Task Negated_a_budget_shorter_than_the_grace_window_is_still_satisfiable()
    {
        var screen = Scripted(_ => []);
        await Drive(Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(400)));
        Assert.InRange(_time.Elapsed, TimeSpan.FromMilliseconds(400), Grace);
    }

    [Fact]
    public async Task Negated_passes_only_after_the_grace_window_holds_continuously()
    {
        var screen = Scripted(_ => []);
        await Drive(Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromSeconds(60)));
        Assert.InRange(_time.Elapsed, Grace, Grace * 2);
    }

    [Fact]
    public async Task Negated_an_undefined_evaluation_resets_the_grace_window()
    {
        // Unchecked for a while, no node to judge on the 8th read, then unchecked again.
        var screen = Scripted(call => call == 8 ? [] : [Unchecked]);
        await Drive(Expect.That(screen.GetByRole("checkbox")).Not.ToBeCheckedAsync(timeout: TimeSpan.FromSeconds(60)));
        Assert.InRange(_time.Elapsed, Grace + TimeSpan.FromMilliseconds(300), Grace * 3);
    }

    [Fact]
    public async Task Negated_a_true_evaluation_clears_accumulated_grace()
    {
        var screen = Scripted(call => call == 8 ? [Shown] : []);
        await Drive(Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromSeconds(60)));
        Assert.InRange(_time.Elapsed, Grace + TimeSpan.FromMilliseconds(300), Grace * 3);
    }

    [Fact]
    public async Task Negated_a_slow_first_read_counts_toward_a_budget_shorter_than_the_grace_window()
    {
        var issued = new List<TimeSpan>();
        var screen = Slow(issued, first: 80, rest: 10);
        await Drive(Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(500)));
        Assert.InRange(_time.Elapsed, TimeSpan.FromMilliseconds(500), Grace);
        Assert.All(issued, at => Assert.True(at < TimeSpan.FromMilliseconds(500), "a read was issued at " + at));
    }

    [Fact]
    public async Task Negated_a_slow_first_read_counts_toward_the_grace_window_under_a_longer_budget()
    {
        var screen = Slow([], first: 80, rest: 10);
        await Drive(Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(1500)));
        Assert.InRange(_time.Elapsed, Grace, TimeSpan.FromMilliseconds(1500));
    }

    [Fact]
    public async Task Negated_fast_reads_under_a_short_budget_pass_at_the_deadline_not_before()
    {
        var screen = Slow([], first: 0, rest: 0);
        await Drive(Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(500)));
        Assert.InRange(_time.Elapsed, TimeSpan.FromMilliseconds(500), Grace);
    }

    [Fact]
    public async Task Negated_a_budget_shorter_than_the_grace_window_still_fails_when_the_positive_state_was_seen()
    {
        var calls = 0;
        var screen = Scripted(call =>
        {
            calls = call;
            return call == 3 ? [Shown] : [];
        });
        var error = await Assert.ThrowsAsync<TestException>(() => Drive(Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(500))));
        Assert.Equal("ASSERTION_FAILED", error.Code);

        // It decided at the deadline instead of reading past it: one read per 50 ms sample, the last at the deadline.
        Assert.InRange(calls, 8, 11);
    }

    [Fact]
    public async Task Returns_once_the_positive_condition_holds()
    {
        var calls = 0;
        var screen = Scripted(call =>
        {
            calls = call;
            return call >= 3 ? [Shown] : [];
        });
        await Drive(Expect.That(screen.GetByRole("status")).ToBeVisibleAsync(timeout: TimeSpan.FromSeconds(5)));
        Assert.Equal(3, calls);
    }

    [Fact]
    public async Task Stops_polling_when_the_signal_aborts_between_evaluations()
    {
        var screen = Scripted(_ => []);
        using var abort = new CancellationTokenSource();
        var poll = Expect.That(screen.GetByRole("status")).ToBeVisibleAsync(timeout: TimeSpan.FromSeconds(60), cancellationToken: abort.Token);
        while (!_time.HasTimer && !poll.IsCompleted)
        {
            await Task.Delay(1);
        }

        await abort.CancelAsync();
        var error = await Assert.ThrowsAsync<TestException>(() => poll);
        Assert.Equal("CANCELLED", error.Code);
    }

    [Fact]
    public async Task Ends_on_the_last_sample_when_the_deadline_cuts_a_read_off_and_fails_on_a_read_the_deadline_did_not_cut()
    {
        var cut = new EngineException("OPERATION_TIMEOUT", "locate timed out");
        var budget = TimeSpan.FromMilliseconds(350);
        var calls = 0;
        var screen = Over(_ =>
        {
            calls++;
            return _time.Elapsed < budget ? Task.FromResult<SemanticNode[]>([]) : throw cut;
        });
        var error = await Assert.ThrowsAsync<TestException>(() => Drive(Expect.That(screen.GetByRole("status")).ToBeVisibleAsync(timeout: budget)));
        Assert.Equal("ASSERTION_FAILED", error.Code);
        Assert.True(calls > 1);

        var early = Over(_ => throw cut);
        Assert.Same(cut, await Assert.ThrowsAsync<EngineException>(() => Drive(Expect.That(early.GetByRole("status")).ToBeVisibleAsync(timeout: TimeSpan.FromSeconds(5)))));
    }

    [Fact]
    public async Task Keeps_a_read_that_hung_most_of_the_wait_a_failure_and_never_lets_a_negation_pass_on_it()
    {
        var cut = new EngineException("OPERATION_TIMEOUT", "locate timed out");
        var budget = TimeSpan.FromMilliseconds(2_000);
        var calls = 0;
        var screen = Over(async token =>
        {
            calls++;
            if (calls == 1)
            {
                return [];
            }

            // The page froze: this read takes the rest of the wait and is cut at its deadline.
            var rest = budget - _time.Elapsed;
            await Task.Delay(rest > TimeSpan.Zero ? rest : TimeSpan.Zero, _time, token);
            throw cut;
        });
        Assert.Same(cut, await Assert.ThrowsAsync<EngineException>(() => Drive(Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: budget))));
    }

    [Fact]
    public async Task Negated_a_read_the_deadline_cut_off_ends_the_poll_on_what_held_until_it_was_cut()
    {
        var cut = new EngineException("OPERATION_TIMEOUT", "locate timed out");
        Task Run(int cutAfterMs)
        {
            var budget = TimeSpan.FromMilliseconds(1_950);
            var from = _time.Elapsed;

            // Visible for the first cutAfterMs, then gone; the read that starts with under a 100 ms tick left is cut.
            var screen = Over(_ =>
            {
                if (budget - (_time.Elapsed - from) < TimeSpan.FromMilliseconds(100))
                {
                    throw cut;
                }

                return Task.FromResult<SemanticNode[]>((_time.Elapsed - from).TotalMilliseconds < cutAfterMs ? [Shown] : []);
            });
            return Drive(Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: budget));
        }

        // Gone from 900 ms: the grace window had run when the read was cut off at 1900 ms.
        await Run(900);

        // Gone from 1000 ms: it had not, and the poll fails with the cut-off read as its cause.
        var error = await Assert.ThrowsAsync<TestException>(() => Run(1_000));
        Assert.Equal("ASSERTION_FAILED", error.Code);
        Assert.Same(cut, error.InnerException);
    }

    [Fact]
    public async Task Negated_a_budget_shorter_than_the_grace_window_still_passes_when_its_last_read_is_cut_off()
    {
        var cut = new EngineException("OPERATION_TIMEOUT", "locate timed out");
        var budget = TimeSpan.FromMilliseconds(350);
        var screen = Over(async token =>
        {
            var rest = budget - _time.Elapsed;
            if (rest < TimeSpan.FromMilliseconds(100))
            {
                await Task.Delay(rest > TimeSpan.Zero ? rest : TimeSpan.Zero, _time, token);
                throw cut;
            }

            return [];
        });
        await Drive(Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: budget));
    }

    private Task Drive(Task pending) => _time.RunAsync(pending);

    /// <summary>A screen whose nth read (from 1) shows the nodes <paramref name="read"/> returns for it.</summary>
    private Screen Scripted(Func<int, SemanticNode[]> read)
    {
        var calls = 0;
        return Over(_ => Task.FromResult(read(Interlocked.Increment(ref calls))));
    }

    /// <summary>A screen showing nothing, whose first read takes <paramref name="first"/> ms and the rest <paramref name="rest"/> ms.</summary>
    private Screen Slow(List<TimeSpan> issued, int first, int rest)
    {
        return Over(async token =>
        {
            issued.Add(_time.Elapsed);
            await Task.Delay(TimeSpan.FromMilliseconds(issued.Count == 1 ? first : rest), _time, token);
            return [];
        });
    }

    private Screen Over(Func<CancellationToken, Task<SemanticNode[]>> read)
    {
        return new Screen(
            async token => new Observation { Route = "/", Roots = await read(token) },
            (_, _, _) => Task.CompletedTask,
            () => CancellationToken.None,
            () => { },
            ScreenFixture.Timeout,
            ScreenFixture.Timeout,
            clock: _time);
    }
}
