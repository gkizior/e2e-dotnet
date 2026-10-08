// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.OAuth;
using E2E.Tests.Fetch;

namespace E2E.Tests.DeviceCode;

[Collection(InstantDeviceFlow.Name)]
public sealed class RunDeviceFlowTests
{
    private static readonly DeviceAuthorization Authorization = new("dev", "ABCD-1234", "https://x/device", null, 60, 5);

    private static Task<DevicePoll<string>> Pending(DeviceAuthorization authorization, CancellationToken cancellationToken) =>
        Task.FromResult(new DevicePoll<string>(DevicePollStatus.Pending));

    private static OAuthLoginCallbacks Callbacks(Action<OAuthAuthInfo>? onAuth = null) =>
        new() { OnAuth = onAuth ?? (_ => { }), OnPrompt = (_, _) => Task.FromResult("") };

    private static async Task<(string Result, List<double> Sleeps, OAuthAuthInfo? Info)> RunAsync(params DevicePoll<string>[] polls)
    {
        var queue = new Queue<DevicePoll<string>>(polls);
        var sleeps = new List<double>();
        OAuthAuthInfo? info = null;
        var old = DeviceFlow.Delay;
        DeviceFlow.Delay = (span, _) =>
        {
            sleeps.Add(Math.Round(span.TotalMilliseconds));
            return Task.CompletedTask;
        };
        try
        {
            var result = await DeviceFlow.RunAsync<string>(
                _ => Task.FromResult(Authorization),
                (_, _) => Task.FromResult(queue.Count > 0 ? queue.Dequeue() : new DevicePoll<string>(DevicePollStatus.Pending)),
                Callbacks(shown => info = shown),
                null,
                CancellationToken.None);
            return (result, sleeps, info);
        }
        finally
        {
            DeviceFlow.Delay = old;
        }
    }

    [Fact]
    public async Task Does_not_start_or_display_a_device_flow_already_cancelled()
    {
        var starts = 0;
        var displays = 0;
        var error = await Assert.ThrowsAsync<OAuthException>(() => DeviceFlow.RunAsync<string>(
            _ =>
            {
                starts++;
                return Task.FromResult(Authorization);
            },
            Pending,
            Callbacks(_ => displays++),
            null,
            new CancellationToken(canceled: true)));
        Assert.Equal(OAuthException.Cancelled, error.Code);
        Assert.Equal(0, starts);
        Assert.Equal(0, displays);
    }

    [Fact]
    public async Task Passes_cancellation_to_the_initial_request_and_reports_a_cancelled_login()
    {
        using var cts = new CancellationTokenSource();
        var error = await Assert.ThrowsAsync<OAuthException>(() => DeviceFlow.RunAsync<string>(
            async token =>
            {
                if (!token.CanBeCanceled)
                {
                    throw new InvalidOperationException("initial request received no token");
                }

                await cts.CancelAsync();
                await Task.Delay(Timeout.Infinite, token);
                return Authorization;
            },
            Pending,
            Callbacks(),
            null,
            cts.Token));
        Assert.Equal(OAuthException.Cancelled, error.Code);
    }

    [Fact]
    public async Task Does_not_display_a_device_code_received_after_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var displays = 0;
        var error = await Assert.ThrowsAsync<OAuthException>(() => DeviceFlow.RunAsync<string>(
            async _ =>
            {
                await cts.CancelAsync();
                return Authorization;
            },
            Pending,
            Callbacks(_ => displays++),
            null,
            cts.Token));
        Assert.Equal(OAuthException.Cancelled, error.Code);
        Assert.Equal(0, displays);
    }

    [Fact]
    public async Task Reports_cancellation_when_an_in_flight_approval_poll_rejects()
    {
        using var cts = new CancellationTokenSource();
        var old = DeviceFlow.Delay;
        DeviceFlow.Delay = (_, _) => Task.CompletedTask;
        try
        {
            var error = await Assert.ThrowsAsync<OAuthException>(() => DeviceFlow.RunAsync<string>(
                _ => Task.FromResult(Authorization),
                async (_, _) =>
                {
                    await cts.CancelAsync();
                    throw new OperationCanceledException();
                },
                Callbacks(),
                null,
                cts.Token));
            Assert.Equal(OAuthException.Cancelled, error.Code);
        }
        finally
        {
            DeviceFlow.Delay = old;
        }
    }

    [Fact]
    public async Task Preserves_a_non_cancellation_initial_request_failure()
    {
        var failure = new InvalidOperationException("device endpoint failed");
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => DeviceFlow.RunAsync<string>(
            _ => throw failure,
            Pending,
            Callbacks(),
            null,
            CancellationToken.None));
        Assert.Same(failure, thrown);
    }

    [Fact]
    public async Task Polls_through_pending_and_slow_down_then_returns_the_grant()
    {
        var (result, sleeps, info) = await RunAsync(
            new DevicePoll<string>(DevicePollStatus.Pending),
            new DevicePoll<string>(DevicePollStatus.SlowDown),
            new DevicePoll<string>(DevicePollStatus.Pending),
            new DevicePoll<string>(DevicePollStatus.Granted, "tok"));
        Assert.Equal("tok", result);
        Assert.Equal(("https://x/device", "ABCD-1234"), (info!.Url, info.UserCode));

        // 5 s interval, then +5 s after slow_down.
        Assert.Equal([5000, 5000, 10000, 10000], sleeps);
    }

    [Fact]
    public async Task Honours_the_interval_a_slow_down_names()
    {
        var (_, sleeps, _) = await RunAsync(
            new DevicePoll<string>(DevicePollStatus.SlowDown, IntervalSeconds: 2),
            new DevicePoll<string>(DevicePollStatus.Granted, "ok"));
        Assert.Equal([5000, 2000], sleeps);
    }

    [Fact]
    public async Task Turns_denial_and_expiry_into_OAuthExceptions()
    {
        Assert.Equal(OAuthException.Cancelled, (await Assert.ThrowsAsync<OAuthException>(() => RunAsync(new DevicePoll<string>(DevicePollStatus.Denied)))).Code);
        Assert.Equal(OAuthException.Timeout, (await Assert.ThrowsAsync<OAuthException>(() => RunAsync(new DevicePoll<string>(DevicePollStatus.Expired)))).Code);
    }

    [Fact]
    public async Task Stops_when_the_device_code_lifetime_runs_out()
    {
        // The port reads the system clock, so a short lifetime stands in for upstream's fake one.
        var old = DeviceFlow.Delay;
        DeviceFlow.Delay = (_, _) => Task.CompletedTask;
        try
        {
            var error = await Assert.ThrowsAsync<OAuthException>(() => DeviceFlow.RunAsync<string>(
                _ => Task.FromResult(Authorization with { ExpiresIn = 0.05 }),
                Pending,
                Callbacks(),
                null,
                CancellationToken.None));
            Assert.Equal(OAuthException.Timeout, error.Code);
        }
        finally
        {
            DeviceFlow.Delay = old;
        }
    }

    [Fact]
    public async Task Stops_at_an_abort_signal()
    {
        using var cts = new CancellationTokenSource();
        var error = await Assert.ThrowsAsync<OAuthException>(() => DeviceFlow.RunAsync<string>(
            _ => Task.FromResult(Authorization),
            Pending,
            Callbacks(_ => cts.Cancel()),
            null,
            cts.Token));
        Assert.Equal(OAuthException.Cancelled, error.Code);
    }
}
