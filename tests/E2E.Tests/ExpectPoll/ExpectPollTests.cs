// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.ExpectPoll;

/// <summary>
/// The port's poll has the toBe and toSatisfy matchers only, so "runs every value matcher", "polls the new
/// matchers until they hold and reports their last failure", "treats a matcher type complaint as not yet, like
/// any other failing sample", and "a negated global regexp never passes on an unchanged matching value" are not
/// ported. The port has no attempt budget, so the nested "inside an attempt" describe is not ported either.
/// </summary>
public sealed class ExpectPollTests
{
    private readonly SteppingTime _time = new();

    [Fact]
    public async Task Passes_once_the_value_settles_and_stops_reading()
    {
        var status = new Settling<string>("running", "done", 3);
        await _time.RunAsync(Expect.Poll(status.Read, Every(5)).ToBeAsync("done"));
        Assert.Equal(4, status.Reads);
    }

    [Fact]
    public async Task Accepts_an_asynchronous_read()
    {
        var count = new Settling<int>(0, 2, 2);
        await _time.RunAsync(Expect.Poll(async () =>
        {
            await Task.Yield();
            return count.Read();
        }, Every(5)).ToSatisfyAsync(value => value > 1, "greater than 1"));
        Assert.Equal(3, count.Reads);
    }

    [Fact]
    public async Task Times_out_with_the_last_value_in_the_message()
    {
        var message = await FailsWith(() => Expect.Poll(() => "running", Options(100, 10)).ToBeAsync("done"));
        Assert.StartsWith("expect.poll(...).toBe(...) timed out after 100 ms\n", message, StringComparison.Ordinal);
        Assert.EndsWith("last: received \"running\", expected \"done\"", message, StringComparison.Ordinal);
        Assert.Equal(2, message.Split('\n').Length);
    }

    [Fact]
    public async Task Not_flips_the_check()
    {
        var status = new Settling<string>("running", "done", 2);
        await _time.RunAsync(Expect.Poll(status.Read, Every(5)).Not.ToBeAsync("running"));
        Assert.Equal(3, status.Reads);

        var message = await FailsWith(() => Expect.Poll(() => "running", Options(60, 10)).Not.ToBeAsync("running"));
        Assert.StartsWith("expect.poll(...).not.toBe(...) timed out after 60 ms", message, StringComparison.Ordinal);
        Assert.Contains("last: received \"running\", expected not \"running\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Places_the_message_option_between_the_matcher_line_and_the_last_sample()
    {
        var options = new PollOptions { Timeout = TimeSpan.FromMilliseconds(60), Interval = TimeSpan.FromMilliseconds(10), Clock = _time, Message = "the batch never finished" };
        var message = await FailsWith(() => Expect.Poll(() => "running", options).ToBeAsync("done"));
        Assert.Equal(
            [
                "expect.poll(...).toBe(...) timed out after 60 ms",
                "the batch never finished",
                "last: received \"running\", expected \"done\"",
            ],
            message.Split('\n'));
    }

    [Fact]
    public async Task Keeps_polling_through_a_throwing_read_and_reports_its_error_at_the_deadline()
    {
        var reads = 0;
        await _time.RunAsync(Expect.Poll(() => ++reads < 3 ? throw new InvalidOperationException("connection refused") : "done", Every(5)).ToBeAsync("done"));
        Assert.Equal(3, reads);

        var failures = 0;
        Func<Task<string>> refused = async () =>
        {
            await Task.Yield();
            failures++;
            throw new InvalidOperationException("connection refused");
        };
        var message = await FailsWith(() => Expect.Poll(refused, Options(80, 10)).ToBeAsync("done"));
        Assert.EndsWith("last: connection refused", message, StringComparison.Ordinal);
        Assert.True(failures > 1);
    }

    [Fact]
    public async Task Bounds_a_hung_read_by_the_deadline()
    {
        var message = await FailsWith(() => Expect.Poll(_ => new TaskCompletionSource<string>().Task, new PollOptions { Timeout = TimeSpan.FromMilliseconds(100), Clock = _time }).ToBeAsync("done"));
        Assert.Contains("timed out after 100 ms", message, StringComparison.Ordinal);
        Assert.EndsWith("last: no read completed", message, StringComparison.Ordinal);
        Assert.Equal(TimeSpan.FromMilliseconds(100), _time.Elapsed);
    }

    [Fact]
    public async Task Keeps_the_last_completed_sample_when_a_later_read_hangs()
    {
        var reads = 0;
        var message = await FailsWith(() => Expect.Poll(_ => ++reads == 1 ? Task.FromResult("running") : new TaskCompletionSource<string>().Task, Options(100, 5)).ToBeAsync("done"));
        Assert.EndsWith("last: received \"running\", expected \"done\"", message, StringComparison.Ordinal);
        Assert.Equal(2, reads);
    }

    [Fact]
    public async Task Honors_the_interval_between_samples()
    {
        var reads = 0;
        await FailsWith(() => Expect.Poll(() =>
        {
            reads++;
            return "running";
        }, Options(200, 50)).ToBeAsync("done"));
        Assert.InRange(reads, 3, 6);
    }

    [Fact]
    public void Refuses_a_timeout_or_interval_that_could_never_expire_before_the_first_read()
    {
        Invalid(new PollOptions { Timeout = Timeout.InfiniteTimeSpan }, "timeout");
        Invalid(new PollOptions { Timeout = TimeSpan.FromMilliseconds(-1) }, "timeout");
        Invalid(new PollOptions { Interval = TimeSpan.Zero }, "interval");
        Invalid(new PollOptions { Interval = TimeSpan.FromMilliseconds(-5) }, "interval");
        Expect.Poll(() => 1, new PollOptions { Timeout = TimeSpan.Zero, Interval = TimeSpan.FromMilliseconds(1) });
    }

    private PollOptions Every(int milliseconds) => new() { Interval = TimeSpan.FromMilliseconds(milliseconds), Clock = _time };

    private PollOptions Options(int timeout, int interval) =>
        new() { Timeout = TimeSpan.FromMilliseconds(timeout), Interval = TimeSpan.FromMilliseconds(interval), Clock = _time };

    private async Task<string> FailsWith(Func<Task> run)
    {
        var error = await Assert.ThrowsAsync<TestException>(() => _time.RunAsync(run()));
        Assert.Equal("ASSERTION_FAILED", error.Code);
        return error.Message;
    }

    private static void Invalid(PollOptions options, string option)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Expect.Poll(() => 1, options));
        Assert.Contains(option, error.Message, StringComparison.Ordinal);
    }

    /// <summary>A read whose value settles after <c>settleAfter</c> samples.</summary>
    private sealed class Settling<T>(T before, T after, int settleAfter)
    {
        public int Reads { get; private set; }

        public T Read()
        {
            Reads++;
            return Reads > settleAfter ? after : before;
        }
    }
}
