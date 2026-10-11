// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using LifecycleApp = E2E.Tests.Lifecycle.FixtureApp;

namespace E2E.Tests.CdpAttach;

/// <summary>
/// The CDP-attach seam end to end: a real Chrome is launched out-of-band with remote debugging, the engine
/// attaches to it through <c>CdpEndpoint</c> instead of launching its own, drives a full attempt over that
/// connection, and on dispose detaches without killing the remote the host owns. This is the exact shape a
/// hosted-browser engine (a per-run cloud session) uses.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class WebEngineOverCdpTests
{
    [Fact]
    public async Task Attaches_to_the_remote_drives_an_attempt_and_detaches_on_dispose_without_killing_it()
    {
        using var app = await LifecycleApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        var resolved = 0;
        var engine = new WebEngine(new WebEngineOptions
        {
            Connect = new WebConnectOptions { CdpEndpoint = _ => { resolved++; return Task.FromResult(remote.Endpoint); } },
        });

        await using (var session = await engine.StartAsync(new EngineStartOptions { BaseUrl = app.Url, ActionTimeout = TimeSpan.FromSeconds(30) }, CancellationToken.None))
        {
            Assert.Equal(1, resolved);
            await session.OpenAsync(app.Url, CancellationToken.None);
            // Proof the attempt ran over the attached remote, not a local launch.
            Assert.Equal("Home", await HeadingAsync(session));
            Assert.Equal("/", (await session.ObserveAsync(CancellationToken.None)).Route);
        }

        // Dispose detaches the CDP session; the remote the host owns is still alive.
        Assert.True(remote.Running);
    }

    [Fact]
    public async Task Reacquires_a_dropped_remote_at_the_next_attempt_by_resolving_the_endpoint_again()
    {
        using var app = await LifecycleApp.StartAsync();
        var current = await RemoteChrome.LaunchAsync();
        var resolved = 0;
        var engine = new WebEngine(new WebEngineOptions
        {
            Connect = new WebConnectOptions { CdpEndpoint = _ => { resolved++; return Task.FromResult(current.Endpoint); } },
        });
        try
        {
            await using (var first = await StartAsync(engine, app.Url))
            {
                await first.OpenAsync(app.Url, CancellationToken.None);
                Assert.Equal(1, resolved);
            }

            // The host's session goes away: kill the remote and stand up a fresh one
            // at a new endpoint, the way a per-run cloud session is re-provisioned.
            await current.DisposeAsync();
            current = await RemoteChrome.LaunchAsync();

            // The next attempt must not fail on the dead browser: it reacquires,
            // running the resolver again, and works over the new session.
            await using var second = await StartAsync(engine, app.Url);
            Assert.Equal(2, resolved);
            await second.OpenAsync(app.Url, CancellationToken.None);
            Assert.Equal("Home", await HeadingAsync(second));
        }
        finally
        {
            await current.DisposeAsync();
        }
    }

    [Fact]
    public async Task Resolves_the_endpoint_again_after_a_cancelled_start()
    {
        using var app = await LifecycleApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        var resolved = 0;
        using var controller = new CancellationTokenSource();
        var engine = new WebEngine(new WebEngineOptions
        {
            Connect = new WebConnectOptions
            {
                CdpEndpoint = _ =>
                {
                    resolved++;
                    // Cancel right as the endpoint is handed back, so the attach
                    // completes into an already-cancelled start.
                    controller.Cancel();
                    return Task.FromResult(remote.Endpoint);
                },
            },
        });
        var options = new EngineStartOptions { BaseUrl = app.Url, ActionTimeout = TimeSpan.FromSeconds(30) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => engine.StartAsync(options, controller.Token));
        Assert.Equal(1, resolved);

        // A fresh start must provision again. Were the late browser cached, the engine
        // would hand it back without consulting the resolver.
        await using (var session = await engine.StartAsync(options, CancellationToken.None))
        {
            Assert.Equal(2, resolved);
        }

        // And the remote the host owns was detached, not killed.
        Assert.True(remote.Running);
    }

    private static Task<IEngineSession> StartAsync(WebEngine engine, string url) =>
        engine.StartAsync(new EngineStartOptions { BaseUrl = url, ActionTimeout = TimeSpan.FromSeconds(30) }, CancellationToken.None);

    private static async Task<string?> HeadingAsync(IEngineSession session) =>
        WebSemanticsTests.Flatten((await session.ObserveAsync(CancellationToken.None)).Roots).First(node => node.Role == "heading").Name;
}
