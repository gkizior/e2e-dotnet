// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Tests.AttemptSession;
using Microsoft.Extensions.Time.Testing;

namespace E2E.Tests.CdpConnect;

/// <summary>
/// The CDP-attach seam without a real browser: a fake dial fails the test if it is reached. The cancel outcome is an
/// <see cref="OperationCanceledException"/> where upstream reports <c>CANCELLED</c>, as in the other start tests.
/// </summary>
public sealed class PlaywrightSurfaceCdpAttachTests
{
    [Fact]
    public async Task Fails_init_with_ENGINE_FAILURE_when_the_endpoint_resolves_empty_or_the_resolver_throws_without_touching_a_browser()
    {
        var empty = await Assert.ThrowsAsync<EngineException>(() => StartAsync(_ => Task.FromResult("   "), CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", empty.Code);
        Assert.False(empty.Retryable);
        Assert.Contains("connect.cdpEndpoint resolved to an empty CDP endpoint", empty.Message, StringComparison.Ordinal);

        var throwing = await Assert.ThrowsAsync<EngineException>(() => StartAsync(_ => throw new InvalidOperationException("vault down"), CancellationToken.None));
        Assert.Equal("ENGINE_FAILURE", throwing.Code);
        Assert.Contains("vault down", throwing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Hands_the_init_signal_to_the_resolver_and_cancels_promptly_while_it_is_still_pending()
    {
        using var cancel = new CancellationTokenSource();
        var seen = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = StartAsync(
            token =>
            {
                seen.SetResult(token);
                return new TaskCompletionSource<string>().Task;
            },
            cancel.Token);
        var signal = await seen.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(signal.IsCancellationRequested);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.True(signal.IsCancellationRequested);
    }

    [Fact]
    public async Task Honours_an_already_aborted_signal_before_resolving_the_endpoint()
    {
        using var cancel = new CancellationTokenSource();
        await cancel.CancelAsync();
        var resolved = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => StartAsync(
            _ =>
            {
                resolved++;
                return Task.FromResult("ws://localhost:0");
            },
            cancel.Token));
        Assert.Equal(0, resolved);
    }

    private static Task<IEngineSession> StartAsync(Func<CancellationToken, Task<string>> cdpEndpoint, CancellationToken cancellationToken)
    {
        var engine = new WebEngine(new WebEngineOptions { Connect = new WebConnectOptions { CdpEndpoint = cdpEndpoint } })
        {
            CreatePlaywright = FakeRemote.Playwright,
            ConnectCdp = (_, _, _, _) => throw new InvalidOperationException("no browser should be dialed"),
            Clock = new FakeTimeProvider(),
        };
        return engine.StartAsync(new EngineStartOptions(), cancellationToken);
    }
}
