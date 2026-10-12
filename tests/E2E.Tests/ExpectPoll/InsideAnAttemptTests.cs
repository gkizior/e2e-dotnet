// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.ExpectPoll;

/// <summary>
/// The port has no attempt budget, so a poll stops on the <see cref="CancellationToken"/> its caller passes, and it throws
/// <see cref="OperationCanceledException"/> in place of an error with the code CANCELLED. So the upstream its "defaults the
/// timeout to the attempt assertionTimeout until the attempt ends", "caps the poll at the attempt deadline", and "refuses a
/// second live attempt in the same process" are not ported.
/// </summary>
public sealed class InsideAnAttemptTests
{
    [Fact]
    public async Task Stops_reading_the_moment_the_attempt_is_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        var reads = 0;
        var options = new PollOptions { Timeout = TimeSpan.FromSeconds(15), Interval = TimeSpan.FromMilliseconds(5) };
        var poll = Expect.Poll(() =>
        {
            reads++;
            return "running";
        }, options).ToBeAsync("done", cancel.Token);

        cancel.CancelAfter(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poll);

        await Task.Delay(20);
        var readsAtCancel = reads;
        await Task.Delay(50);
        Assert.Equal(readsAtCancel, reads);
    }

    [Fact]
    public async Task Cuts_a_read_in_flight_when_the_attempt_is_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        var options = new PollOptions { Timeout = TimeSpan.FromSeconds(15) };
        var poll = Expect.Poll(_ => new TaskCompletionSource<string>().Task, options).ToBeAsync("done", cancel.Token);

        cancel.CancelAfter(TimeSpan.FromMilliseconds(20));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => poll);
    }
}
