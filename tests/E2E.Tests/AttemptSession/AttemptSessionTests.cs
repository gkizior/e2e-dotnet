// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Engine;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Playwright;

namespace E2E.Tests.AttemptSession;

/// <summary>An attempt owns pending connections and rejects work from retired generations.</summary>
public sealed class AttemptSessionTests
{
    private const string Url = "http://app.test/";

    private static readonly CancellationToken None = CancellationToken.None;

    // Context ids are refused once any attempt in the process rode them, so each test has its own.
    private readonly string _run = Guid.NewGuid().ToString("N");
    private readonly List<IBrowser> _connections = [];
    private readonly List<TimeSpan> _dialTimeouts = [];
    private int _connects;

    [Fact]
    public async Task Detaches_an_unresolved_identity_before_its_response_arrives_and_cannot_overwrite_the_next_attempt()
    {
        var response = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = Remote("old", response.Task);
        var current = Remote("current");
        _connections.AddRange([old.Browser, current.Browser]);
        using var cancel = new CancellationTokenSource();
        var starting = Engine().StartAsync(Start(), cancel.Token);
        await PollAsync(() => old.SendCalls == 1);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        Assert.True(old.CloseCalls >= 1);
        await using var next = await Engine().StartAsync(Start(), None);
        response.SetResult(FakeRemote.Json("""{"browserContextIds":[],"defaultBrowserContextId":"old"}"""));
        await PollAsync(() => old.CloseCalls > 1);
        Assert.Same(current.Context, WebEngine.SurfaceOf(next)!.Context());
    }

    [Fact]
    public async Task Leaves_the_persistent_page_at_the_window_size_under_viewport_null()
    {
        var current = Remote("current");
        _connections.Add(current.Browser);
        var active = FakeTarget.Create("page");
        current.Pages.Add(active.Page);
        await using var owner = await Engine(followWindow: true).StartAsync(Start(), None);
        await owner.OpenAsync(Url, None);
        Assert.Empty(active.ViewportCalls);
    }

    [Fact]
    public async Task Serves_the_first_page_from_the_persistent_browsers_own_tab_on_a_fresh_document_and_opens_a_new_tab_after_it_closed()
    {
        var current = Remote("current");
        var initial = FakeTarget.Create("initial");
        var opened = FakeTarget.Create("opened");
        current.Pages.Add(initial.Page);
        var newPage = 0;
        current.ContextFake.Members["NewPageAsync"] = _ =>
        {
            newPage++;
            return Task.FromResult(opened.Page);
        };
        _connections.Add(current.Browser);
        await using var owner = await Engine().StartAsync(Start(), None);
        await owner.OpenAsync(Url, None);
        Assert.Same(initial.Page, WebEngine.SurfaceOf(owner)!.Page());
        Assert.Equal("about:blank", initial.Gotos[0].Url);
        Assert.Equal(0, newPage);
        initial.Closed = true;
        await owner.OpenAsync(Url, None);
        Assert.Same(opened.Page, WebEngine.SurfaceOf(owner)!.Page());
        Assert.Equal(1, newPage);
    }

    [Fact]
    public async Task Opens_a_new_tab_in_an_ordinary_context_whatever_pages_the_browser_already_shows()
    {
        var existing = FakeTarget.Create("existing").Page;
        var opened = FakeTarget.Create("opened").Page;
        var newPage = 0;
        var (context, contextFake) = Fake<IBrowserContext>.New();
        contextFake.Members["get_Pages"] = _ => (IReadOnlyList<IPage>)[existing];
        contextFake.Members["NewPageAsync"] = _ =>
        {
            newPage++;
            return Task.FromResult(opened);
        };
        contextFake.Members["AddInitScriptAsync"] = _ => Task.FromResult(FakeRemote.Registration);
        contextFake.Members["CloseAsync"] = _ => Task.CompletedTask;
        var (browser, browserFake) = Fake<IBrowser>.New();
        browserFake.Members["NewContextAsync"] = _ => Task.FromResult(context);
        browserFake.Members["CloseAsync"] = _ => Task.CompletedTask;
        _connections.Add(browser);
        var engine = new WebEngine(new WebEngineOptions
        {
            Viewport = new WebViewport(320, 200),
            Connect = new WebConnectOptions { CdpEndpoint = _ => Task.FromResult("provisioned") },
        })
        {
            CreatePlaywright = FakeRemote.Playwright,
            ConnectCdp = ConnectAsync,
            Clock = new FakeTimeProvider(),
        };
        await using var owner = await engine.StartAsync(Start(), None);
        await owner.OpenAsync(Url, None);
        Assert.Same(opened, WebEngine.SurfaceOf(owner)!.Page());
        Assert.Equal(1, newPage);
    }

    [Fact]
    public async Task Refuses_a_viewport_that_is_not_whole_pixels_keeps_a_copy_of_the_one_it_accepts_and_resizes_an_open_page()
    {
        var current = Remote("current");
        _connections.Add(current.Browser);
        var active = FakeTarget.Create("page");
        current.Pages.Add(active.Page);
        await using var owner = await Engine(followWindow: true).StartAsync(Start(), None);
        var browser = (IBrowserSession)owner;
        await browser.SetViewportAsync(390, 600, None);
        Assert.Empty(active.ViewportCalls);
        await owner.OpenAsync(Url, None);
        Assert.NotEmpty(active.ViewportCalls);
        Assert.All(active.ViewportCalls, call => Assert.Equal((390, 600), call));
        await browser.SetViewportAsync(800, 600, None);
        Assert.Equal((800, 600), active.ViewportCalls[^1]);
    }

    [Fact]
    public async Task Does_not_attach_an_endpoint_that_resolves_after_disposal()
    {
        var endpoint = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provisioned = 0;
        using var cancel = new CancellationTokenSource();
        var starting = Engine(provision: _ =>
        {
            provisioned++;
            return endpoint.Task;
        }).StartAsync(Start(), cancel.Token);
        await PollAsync(() => provisioned == 1);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting);
        endpoint.SetResult("late");
        await Task.Delay(10);
        Assert.Equal(0, _connects);
    }

    [Fact]
    public async Task Keeps_a_late_old_page_identity_out_of_the_next_attempt()
    {
        var old = Remote("old");
        var current = Remote("current");
        _connections.AddRange([old.Browser, current.Browser]);
        var first = await Engine().StartAsync(Start(), None);
        var response = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var delayed = FakeTarget.Create("old-page", response.Task);
        old.Pages.Add(delayed.Page);
        var opening = first.OpenAsync(Url, None);
        await PollAsync(() => delayed.SendCalls == 1);
        await first.DisposeAsync();
        await using var next = await Engine().StartAsync(Start(), None);
        var active = FakeTarget.Create("current-page");
        current.Pages.Add(active.Page);
        await next.OpenAsync(Url, None);
        response.SetResult(FakeRemote.Json("""{"targetInfo":{"targetId":"old-page"}}"""));
        Assert.Equal("NODE_STALE", (await Assert.ThrowsAsync<EngineException>(() => opening)).Code);
        Assert.Same(active.Page, WebEngine.SurfaceOf(next)!.Page());
    }

    [Fact]
    public async Task Does_not_publish_an_attachment_until_configuration_succeeds()
    {
        var configure = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var browser = Remote("current");
        browser.ContextFake.Members["AddInitScriptAsync"] = _ => configure.Task.ContinueWith(_ => FakeRemote.Registration, TaskScheduler.Default);
        _connections.Add(browser.Browser);
        var starting = Engine().StartAsync(Start(), None);
        await PollAsync(() => browser.SendCalls == 1);
        Assert.False(starting.IsCompleted);
        configure.SetResult();
        await using var first = await starting;
        Assert.Same(browser.Context, WebEngine.SurfaceOf(first)!.Context());
    }

    [Theory]
    [InlineData("timeout")]
    [InlineData("cancel")]
    public async Task Distinguishes_recovery_and_prevents_dispatch(string reason)
    {
        var browser = Remote("current");
        var page = FakeTarget.Create("page");
        browser.Pages.Add(page.Page);
        _connections.Add(browser.Browser);
        CancellationToken? resolverToken = null;
        var clock = new FakeTimeProvider();
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var engine = Engine(clock: clock, reconnect: token =>
        {
            resolverToken = token;
            requested.TrySetResult();
            return new TaskCompletionSource<string>().Task;
        });
        await using var first = await engine.StartAsync(Start(TimeSpan.FromMilliseconds(reason == "timeout" ? 30 : 1_000)), None);
        browser.Disconnect();
        using var caller = new CancellationTokenSource();
        var code = reason == "timeout" ? "OPERATION_TIMEOUT" : "CANCELLED";
        var result = first.OpenAsync(Url, caller.Token);
        await requested.Task;
        if (reason == "cancel")
        {
            await caller.CancelAsync();

            // The port reports a caller's own cancellation as OperationCanceledException.
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => result);
        }
        else
        {
            // The deadline timer starts before the resolver runs, so this fires it.
            clock.Advance(TimeSpan.FromMilliseconds(30));
            Assert.Equal(code, (await Assert.ThrowsAsync<EngineException>(() => result)).Code);
        }

        Assert.Equal(reason == "cancel", caller.IsCancellationRequested);
        Assert.True(resolverToken?.IsCancellationRequested);
        Assert.Empty(page.Gotos);
        Assert.Equal(code, (await Assert.ThrowsAsync<EngineException>(() => first.OpenAsync(Url, None))).Code);
    }

    [Fact]
    public async Task Refuses_a_remote_that_cannot_expose_its_persistent_context_identity()
    {
        var unsupported = FakeRemote.Create();
        _connections.Add(unsupported.Browser);
        var error = await Assert.ThrowsAsync<EngineException>(() => Engine().StartAsync(Start(), None));
        Assert.Contains("context identity is unavailable", error.Message, StringComparison.Ordinal);
        Assert.True(unsupported.CloseCalls > 0);
    }

    [Fact]
    public async Task Propagates_the_recovery_owner_cancellation_to_another_waiting_operation()
    {
        var browser = Remote("current");
        var page = FakeTarget.Create("page");
        browser.Pages.Add(page.Page);
        _connections.Add(browser.Browser);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var first = await Engine(reconnect: _ =>
        {
            requested.TrySetResult();
            return new TaskCompletionSource<string>().Task;
        }).StartAsync(Start(), None);
        browser.Disconnect();
        using var controller = new CancellationTokenSource();
        var owner = first.OpenAsync(Url, controller.Token);
        await requested.Task;
        var waiter = first.OpenAsync(Url, None);
        await controller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner);
        Assert.Equal("CANCELLED", (await Assert.ThrowsAsync<EngineException>(() => waiter)).Code);
        Assert.Empty(page.Gotos);
    }

    [Fact]
    public async Task Subtracts_endpoint_resolution_from_both_the_CDP_dial_and_dispatched_operation_budgets()
    {
        var original = Remote("current");
        var recovered = Remote("current");
        var page = FakeTarget.Create("page");
        recovered.Pages.Add(page.Page);
        _connections.AddRange([original.Browser, recovered.Browser]);
        var clock = new FakeTimeProvider();
        await using var first = await Engine(clock: clock, reconnect: _ =>
        {
            clock.Advance(TimeSpan.FromMilliseconds(40));
            return Task.FromResult("existing");
        }).StartAsync(Start(TimeSpan.FromMilliseconds(200)), None);
        original.Disconnect();
        await first.OpenAsync(Url, None);
        Assert.Equal(160, _dialTimeouts[1].TotalMilliseconds);

        // 160 ms left, less Playwright's timeout lead: half of a budget this short.
        Assert.Equal(80, page.Gotos.Single(navigation => navigation.Url == Url).Timeout);
    }

    private FakeRemote Remote(string identity, Task<JsonElement?>? response = null) => FakeRemote.Create(identity + "-" + _run, response);

    private static EngineStartOptions Start(TimeSpan? actionTimeout = null) =>
        new() { BaseUrl = Url, ActionTimeout = actionTimeout ?? TimeSpan.FromSeconds(1) };

    private static async Task PollAsync(Func<bool> condition)
    {
        for (var tries = 0; !condition() && tries < 200; tries++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    /// <summary>
    /// Creates a separate owner whose engine remembers the remote contexts its attempts used. <paramref name="followWindow"/> is <c>viewport: null</c>.
    /// Time moves only when a test advances <paramref name="clock"/>, so a budget never runs out because the machine is slow.
    /// </summary>
    private WebEngine Engine(
        bool followWindow = false,
        Func<CancellationToken, Task<string>>? provision = null,
        Func<CancellationToken, Task<string>>? reconnect = null,
        TimeProvider? clock = null)
    {
        return new WebEngine(new WebEngineOptions
        {
            Viewport = followWindow ? null : new WebViewport(320, 200),
            Connect = new WebConnectOptions
            {
                CdpEndpoint = provision ?? (_ => Task.FromResult("provisioned")),
                ReconnectEndpoint = reconnect ?? (_ => Task.FromResult("existing")),
            },
        })
        {
            CreatePlaywright = FakeRemote.Playwright,
            ConnectCdp = ConnectAsync,
            Clock = clock ?? new FakeTimeProvider(),
        };
    }

    /// <summary>Hands out the queued remote browsers in order, the last one to every later dial.</summary>
    private Task<IBrowser> ConnectAsync(IPlaywright playwright, string endpoint, TimeSpan timeout, CancellationToken cancellationToken)
    {
        lock (_connections)
        {
            _dialTimeouts.Add(timeout);
            var index = Math.Min(_connects++, _connections.Count - 1);
            return Task.FromResult(_connections[index]);
        }
    }
}
