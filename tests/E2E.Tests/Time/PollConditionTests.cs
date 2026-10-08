// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using E2E.Engine;

namespace E2E.Tests.Time;

/// <summary>
/// Upstream tests <c>pollCondition</c> on fake timers. The port polls inside the
/// locator matchers, so these run a matcher over a screen whose every read is
/// scripted, on the real clock. The port samples every 50 ms, upstream every 100 ms.
/// </summary>
public sealed class PollConditionTests
{
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(1000);

    private static readonly SemanticNode Shown = new() { Ref = "s", Role = "status", Name = "Saving" };

    private static readonly SemanticNode Unchecked = new() { Ref = "c", Role = "checkbox", Name = "Terms" };

    [Fact]
    public async Task Throws_the_caller_error_at_the_deadline()
    {
        var screen = Scripted(_ => []);
        var watch = Stopwatch.StartNew();
        var error = await Assert.ThrowsAsync<TestException>(() => Expect.That(screen.GetByRole("status")).ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(350)));
        Assert.Equal("ASSERTION_FAILED", error.Code);
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(350), TimeSpan.FromMilliseconds(1000));
    }

    [Fact]
    public async Task Negated_a_budget_shorter_than_the_grace_window_is_still_satisfiable()
    {
        var screen = Scripted(_ => []);
        var watch = Stopwatch.StartNew();
        await Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(400));
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(400), Grace);
    }

    [Fact]
    public async Task Negated_passes_only_after_the_grace_window_holds_continuously()
    {
        var screen = Scripted(_ => []);
        var watch = Stopwatch.StartNew();
        await Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromSeconds(60));
        Assert.InRange(watch.Elapsed, Grace, Grace * 2);
    }

    [Fact]
    public async Task Negated_an_undefined_evaluation_resets_the_grace_window()
    {
        // Unchecked for a while, no node to judge on the 8th read, then unchecked again.
        var screen = Scripted(call => call == 8 ? [] : [Unchecked]);
        var watch = Stopwatch.StartNew();
        await Expect.That(screen.GetByRole("checkbox")).Not.ToBeCheckedAsync(timeout: TimeSpan.FromSeconds(60));
        Assert.InRange(watch.Elapsed, Grace + TimeSpan.FromMilliseconds(300), Grace * 3);
    }

    [Fact]
    public async Task Negated_a_true_evaluation_clears_accumulated_grace()
    {
        var screen = Scripted(call => call == 8 ? [Shown] : []);
        var watch = Stopwatch.StartNew();
        await Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromSeconds(60));
        Assert.InRange(watch.Elapsed, Grace + TimeSpan.FromMilliseconds(300), Grace * 3);
    }

    [Fact(Skip = "Gap: the port can issue one more read after the deadline before it decides; upstream decides at the deadline without a read past it.")]
    public async Task Negated_a_slow_first_read_counts_toward_a_budget_shorter_than_the_grace_window()
    {
        var issued = new List<TimeSpan>();
        var watch = Stopwatch.StartNew();
        var screen = Slow(watch, issued, first: 80, rest: 10);
        await Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(500));
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(500), Grace);
        Assert.All(issued, at => Assert.True(at < TimeSpan.FromMilliseconds(500), "a read was issued at " + at));
    }

    [Fact]
    public async Task Negated_a_slow_first_read_counts_toward_the_grace_window_under_a_longer_budget()
    {
        var watch = Stopwatch.StartNew();
        var screen = Slow(watch, [], first: 80, rest: 10);
        await Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(1500));
        Assert.InRange(watch.Elapsed, Grace, TimeSpan.FromMilliseconds(1500));
    }

    [Fact]
    public async Task Negated_fast_reads_under_a_short_budget_pass_at_the_deadline_not_before()
    {
        var watch = Stopwatch.StartNew();
        var screen = Slow(watch, [], first: 0, rest: 0);
        await Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(500));
        Assert.InRange(watch.Elapsed, TimeSpan.FromMilliseconds(500), Grace);
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
        var error = await Assert.ThrowsAsync<TestException>(() => Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(500)));
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
        await Expect.That(screen.GetByRole("status")).ToBeVisibleAsync(timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(3, calls);
    }

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/125: a cancelled matcher throws TaskCanceledException, not CANCELLED")]
    public async Task Stops_polling_when_the_signal_aborts_between_evaluations()
    {
        var screen = Scripted(_ => []);
        using var abort = new CancellationTokenSource();
        var poll = Expect.That(screen.GetByRole("status")).ToBeVisibleAsync(timeout: TimeSpan.FromSeconds(60), cancellationToken: abort.Token);
        await Task.Delay(25);
        await abort.CancelAsync();
        var error = await Assert.ThrowsAsync<TestException>(() => poll);
        Assert.Equal("CANCELLED", error.Code);
    }

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/126: the engine timeout of a cut-off read escapes the matcher")]
    public async Task Ends_on_the_last_sample_when_the_deadline_cuts_a_read_off_and_fails_on_a_read_the_deadline_did_not_cut()
    {
        var cut = new EngineException("OPERATION_TIMEOUT", "locate timed out");
        var budget = TimeSpan.FromMilliseconds(350);
        var watch = Stopwatch.StartNew();
        var calls = 0;
        var screen = Over(_ =>
        {
            calls++;
            return watch.Elapsed < budget ? Task.FromResult<SemanticNode[]>([]) : throw cut;
        });
        var error = await Assert.ThrowsAsync<TestException>(() => Expect.That(screen.GetByRole("status")).ToBeVisibleAsync(timeout: budget));
        Assert.Equal("ASSERTION_FAILED", error.Code);
        Assert.True(calls > 1);

        var early = Over(_ => throw cut);
        Assert.Same(cut, await Assert.ThrowsAsync<EngineException>(() => Expect.That(early.GetByRole("status")).ToBeVisibleAsync(timeout: TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public async Task Keeps_a_read_that_hung_most_of_the_wait_a_failure_and_never_lets_a_negation_pass_on_it()
    {
        var cut = new EngineException("OPERATION_TIMEOUT", "locate timed out");
        var budget = TimeSpan.FromMilliseconds(2_000);
        var watch = Stopwatch.StartNew();
        var calls = 0;
        var screen = Over(async token =>
        {
            calls++;
            if (calls == 1)
            {
                return [];
            }

            // The page froze: this read takes the rest of the wait and is cut at its deadline.
            var rest = budget - watch.Elapsed;
            await Task.Delay(rest > TimeSpan.Zero ? rest : TimeSpan.Zero, token);
            throw cut;
        });
        Assert.Same(cut, await Assert.ThrowsAsync<EngineException>(() => Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: budget)));
    }

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/126: the engine timeout of a cut-off read escapes the matcher")]
    public async Task Negated_a_read_the_deadline_cut_off_ends_the_poll_on_what_held_until_it_was_cut()
    {
        var cut = new EngineException("OPERATION_TIMEOUT", "locate timed out");
        Task Run(int cutAfterMs)
        {
            var budget = TimeSpan.FromMilliseconds(1_950);
            var watch = Stopwatch.StartNew();

            // Visible for the first cutAfterMs, then gone; the read that starts with under a 100 ms tick left is cut.
            var screen = Over(_ =>
            {
                if (budget - watch.Elapsed < TimeSpan.FromMilliseconds(100))
                {
                    throw cut;
                }

                return Task.FromResult<SemanticNode[]>(watch.ElapsedMilliseconds < cutAfterMs ? [Shown] : []);
            });
            return Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: budget);
        }

        // Gone from 900 ms: the grace window had run when the read was cut off at 1900 ms.
        await Run(900);

        // Gone from 1000 ms: it had not, and the poll fails with the cut-off read as its cause.
        var error = await Assert.ThrowsAsync<TestException>(() => Run(1_000));
        Assert.Equal("ASSERTION_FAILED", error.Code);
        Assert.Same(cut, error.InnerException);
    }

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/126: the engine timeout of a cut-off read escapes the matcher")]
    public async Task Negated_a_budget_shorter_than_the_grace_window_still_passes_when_its_last_read_is_cut_off()
    {
        var cut = new EngineException("OPERATION_TIMEOUT", "locate timed out");
        var budget = TimeSpan.FromMilliseconds(350);
        var watch = Stopwatch.StartNew();
        var screen = Over(async token =>
        {
            var rest = budget - watch.Elapsed;
            if (rest < TimeSpan.FromMilliseconds(100))
            {
                await Task.Delay(rest > TimeSpan.Zero ? rest : TimeSpan.Zero, token);
                throw cut;
            }

            return [];
        });
        await Expect.That(screen.GetByRole("status")).Not.ToBeVisibleAsync(timeout: budget);
    }

    /// <summary>A screen whose nth read (from 1) shows the nodes <paramref name="read"/> returns for it.</summary>
    private static Screen Scripted(Func<int, SemanticNode[]> read)
    {
        var calls = 0;
        return Over(_ => Task.FromResult(read(Interlocked.Increment(ref calls))));
    }

    /// <summary>A screen showing nothing, whose first read takes <paramref name="first"/> ms and the rest <paramref name="rest"/> ms.</summary>
    private static Screen Slow(Stopwatch watch, List<TimeSpan> issued, int first, int rest)
    {
        return Over(async token =>
        {
            issued.Add(watch.Elapsed);
            await Task.Delay(issued.Count == 1 ? first : rest, token);
            return [];
        });
    }

    private static Screen Over(Func<CancellationToken, Task<SemanticNode[]>> read)
    {
        return new Screen(
            async token => new Observation { Route = "/", Roots = await read(token) },
            (_, _, _) => Task.CompletedTask,
            () => CancellationToken.None,
            () => { },
            ScreenFixture.Timeout,
            ScreenFixture.Timeout);
    }
}
