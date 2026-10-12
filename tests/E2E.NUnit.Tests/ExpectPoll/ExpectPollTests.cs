// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.NUnit.Tests.ExpectPoll;

/// <summary>
/// Ported from upstream's <c>expect-poll.test.ts</c>. The port has no value matchers such as <c>toEqual</c> or
/// <c>toHaveProperty</c>, so these tests poll with NUnit constraints through <c>ToMatchAsync</c> in their place.
/// </summary>
public sealed class ExpectPollTests
{
    private static PollOptions Every(int milliseconds) => new() { Interval = TimeSpan.FromMilliseconds(milliseconds) };

    private static PollOptions Within(int timeout, int interval) =>
        new() { Timeout = TimeSpan.FromMilliseconds(timeout), Interval = TimeSpan.FromMilliseconds(interval) };

    [Test]
    public async Task Runs_every_value_matcher_as_an_NUnit_constraint()
    {
        // Partial: upstream runs its own matchers (toMatchObject, toHaveProperty, asymmetric matchers); the port runs the NUnit constraint of the same job.
        var reads = 0;
        string[] Rows() => ++reads > 1 ? ["a", "b"] : [];

        await Expect.Poll(Rows, Every(5)).ToMatchAsync(Is.EqualTo(new[] { "a", "b" }));
        await Expect.Poll(Rows, Every(5)).ToMatchAsync(Contains.Item("b"));
        await Expect.Poll(() => Rows().Length, Every(5)).ToMatchAsync(Is.GreaterThan(1));
        await Expect.Poll(() => "order #42", Every(5)).ToMatchAsync(Does.Match(@"#\d+"));
        await Expect.Poll(() => (string?)null, Every(5)).ToMatchAsync(Is.Null);
        await Expect.Poll(() => 0, Every(5)).ToMatchAsync(Is.Zero);
        await Expect.Poll(() => 1, Every(5)).ToMatchAsync(Is.Not.Zero);
        await Expect.Poll(() => "x", Every(5)).ToMatchAsync(Is.Not.Null);
        await Expect.Poll(() => 1, Every(5)).ToMatchAsync(Is.LessThan(2));
        await Expect.Poll(Rows, Every(5)).ToMatchAsync(Has.Length.EqualTo(2));
        await Expect.Poll(Rows, Every(5)).ToMatchAsync(Has.Some.EqualTo("a"));
    }

    [Test]
    public async Task Polls_the_constraints_until_they_hold_and_reports_their_last_failure()
    {
        // Partial: the failure text is the port's "received ..., which does not satisfy ...", not upstream's matcher sentence.
        var reads = 0;
        await Expect.Poll(() => ++reads > 2 ? new[] { "a", "b" } : ["a"], Every(5)).ToMatchAsync(Has.Length.EqualTo(2));
        Assert.That(reads, Is.EqualTo(3));

        var running = await Assert.ThrowsAsync<TestException>(() => Expect.Poll(() => "running", Within(60, 10)).ToMatchAsync(Is.EqualTo("done")));
        Assert.That(running!.Code, Is.EqualTo("ASSERTION_FAILED"));
        Assert.That(running.Message, Does.StartWith("expect.poll(...).toSatisfy(...) timed out after 60 ms"));
        Assert.That(running.Message, Does.Contain("last: received \"running\", which does not satisfy"));

        var held = await Assert.ThrowsAsync<TestException>(() => Expect.Poll(() => 1, Within(60, 10)).Not.ToMatchAsync(Is.EqualTo(1)));
        Assert.That(held!.Message, Does.StartWith("expect.poll(...).not.toSatisfy(...) timed out after 60 ms"));
        Assert.That(held.Message, Does.Contain("last: received 1, which does satisfy"));
    }
}
