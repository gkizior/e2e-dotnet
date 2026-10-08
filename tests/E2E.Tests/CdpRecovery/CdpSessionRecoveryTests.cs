// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Engine;
using Microsoft.Playwright;

namespace E2E.Tests.CdpRecovery;

/// <summary>Real CDP transport drops preserve only the dedicated persistent context, never a replacement.</summary>
[Collection(BrowserCollection.Name)]
public sealed class CdpSessionRecoveryTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task Keeps_the_exact_page_document_state_storage_viewport_routes_and_dialog_handlers()
    {
        using var app = await FixtureApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        var provisioned = 0;
        var reconnected = 0;
        var session = await StartAsync(app, new WebConnectOptions
        {
            CdpEndpoint = _ => { provisioned++; return Task.FromResult(remote.Endpoint); },
            ReconnectEndpoint = _ => { reconnected++; return Task.FromResult(remote.Endpoint); },
        });
        var live = WebEngine.SurfaceOf(session)!;
        try
        {
            var original = live.Page();
            await original.SetViewportSizeAsync(800, 600);
            await original.EvaluateAsync("""
                () => {
                  sessionStorage.setItem('attempt', 'kept');
                  localStorage.setItem('login', 'kept');
                  document.cookie = 'session=kept';
                  document.querySelector('input[name=user]').value = 'unsaved';
                }
                """);
            var stale = Find(await session.ObserveAsync(None), node => node.Role == "heading");

            // Another tab at the same URL makes URL-based selection ambiguous.
            var other = await live.Context().NewPageAsync();
            await other.GotoAsync(original.Url);
            await other.Locator("h1").EvaluateAsync("node => { node.textContent = 'Wrong tab'; }");

            await ((IBrowserSession)session).RouteAsync(
                global::E2E.Internal.Routes.Matcher("**/kept-route"),
                route => route.FulfillAsync(new RouteFulfillResponse { Status = 200, Body = "route kept" }),
                None);
            await original.Context.Browser!.CloseAsync();

            Assert.Equal("Login", await EvaluateAsync<string>(session, "() => document.querySelector('h1').textContent"));
            var recovered = live.Page();
            Assert.NotSame(original, recovered);
            Assert.Equal("unsaved", await recovered.Locator("input[name=user]").InputValueAsync());
            Assert.Equal(["kept", "kept", "session=kept"], await recovered.EvaluateAsync<string[]>("() => [sessionStorage.getItem('attempt'), localStorage.getItem('login'), document.cookie]"));
            Assert.Equal(800, recovered.ViewportSize!.Width);
            Assert.Equal(600, recovered.ViewportSize!.Height);
            Assert.Equal("route kept", await recovered.EvaluateAsync<string>("async () => (await fetch('/kept-route')).text()"));
            Assert.Equal("NODE_STALE", (await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(stale, new LocatorAction.Tap(), None))).Code);
            Assert.Equal("NODE_STALE", (await Assert.ThrowsAsync<EngineException>(() => session.SwipeAsync(ScrollDirection.Down, None))).Code);
            await session.ObserveAsync(None);
            Assert.Equal(1, provisioned);
            Assert.Equal(1, reconnected);
        }
        finally
        {
            var browser = live.Context().Browser!;
            await session.DisposeAsync();
            Assert.False(browser.IsConnected);
            Assert.True(remote.Running);
        }
    }

    [Fact]
    public async Task Runs_configured_and_test_init_scripts_once_per_document_after_reconnect_too()
    {
        using var app = await FixtureApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        await using var session = await StartAsync(app, new WebEngine(new WebEngineOptions
        {
            Connect = Fixed(remote),
            InitScripts = ["(window.trail ??= []).push('config');"],
        }));
        Task<string[]?> Trail() => EvaluateAsync<string[]>(session, "() => window.trail ?? null");
        Assert.Equal<string[]>(["config"], await Trail());
        var test = await WebInitScript.FromFunction("() => { (window.trail ??= []).push('test'); }")
            .ReadAsync(null, Directory.GetCurrentDirectory(), (message, cause) => new InvalidOperationException(message, cause), None);
        await ((IBrowserSession)session).AddInitScriptAsync(test, None);
        await session.OpenAsync(app.Url + "login", None);
        Assert.Equal<string[]>(["config", "test"], await Trail());

        await WebEngine.SurfaceOf(session)!.Context().Browser!.CloseAsync();
        await session.OpenAsync(app.Url + "login", None);
        Assert.Equal<string[]>(["config", "test"], await Trail());
    }

    [Fact]
    public async Task Fails_an_uncertain_click_without_reconnecting_or_dispatching_it_twice()
    {
        using var app = await FixtureApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        var reconnected = 0;
        await using var session = await StartAsync(app, new WebConnectOptions
        {
            CdpEndpoint = _ => Task.FromResult(remote.Endpoint),
            ReconnectEndpoint = _ => { reconnected++; return Task.FromResult(remote.Endpoint); },
        });
        var page = WebEngine.SurfaceOf(session)!.Page();
        await page.EvaluateAsync("""
            () => {
              sessionStorage.setItem('clicks', '0');
              const button = document.createElement('button');
              button.textContent = 'Purchase';
              button.addEventListener('click', () => {
                sessionStorage.setItem('clicks', String(Number(sessionStorage.getItem('clicks')) + 1));
                // Pause after the mutation so the transport disappears before click completion.
                debugger;
              });
              document.body.append(button);
            }
            """);
        var debuggerSession = await page.Context.NewCDPSessionAsync(page);
        await debuggerSession.SendAsync("Debugger.enable");
        debuggerSession.Event("Debugger.paused").OnEvent += (_, _) => _ = page.Context.Browser!.CloseAsync();
        var button = Find(await session.ObserveAsync(None), node => node.Role == "button" && node.Name == "Purchase");
        var error = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(button, new LocatorAction.Tap(), None));
        Assert.False(error.Retryable);
        Assert.Equal(0, reconnected);
        await session.ObserveAsync(None);
        Assert.Equal("1", await WebEngine.SurfaceOf(session)!.Page().EvaluateAsync<string>("() => sessionStorage.getItem('clicks')"));
        Assert.Equal(1, reconnected);
    }

    [Fact]
    public async Task Rejects_a_different_browser_and_never_falls_back_to_the_provisioning_resolver()
    {
        using var app = await FixtureApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        await using var replacement = await RemoteChrome.LaunchAsync();
        var provisioned = 0;
        await using var session = await StartAsync(app, new WebConnectOptions
        {
            CdpEndpoint = _ => { provisioned++; return Task.FromResult(remote.Endpoint); },
            ReconnectEndpoint = _ => Task.FromResult(replacement.Endpoint),
        });
        await WebEngine.SurfaceOf(session)!.Context().Browser!.CloseAsync();
        Assert.Contains("different browser", (await Assert.ThrowsAsync<EngineException>(() => session.ObserveAsync(None))).Message, StringComparison.Ordinal);
        Assert.Contains("different browser", (await Assert.ThrowsAsync<EngineException>(() => session.ObserveAsync(None))).Message, StringComparison.Ordinal);
        Assert.Equal(1, provisioned);
        Assert.True(replacement.Running);
    }

    [Fact]
    public async Task Rejects_a_missing_active_target_even_when_another_tab_has_the_same_url()
    {
        using var app = await FixtureApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        await using var session = await StartAsync(app, Fixed(remote));
        var page = WebEngine.SurfaceOf(session)!.Page();
        var other = await page.Context.NewPageAsync();
        await other.GotoAsync(page.Url);
        await page.CloseAsync();
        await page.Context.Browser!.CloseAsync();
        Assert.Contains("original page no longer exists", (await Assert.ThrowsAsync<EngineException>(() => session.ObserveAsync(None))).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bounds_a_hung_reconnect_resolver_and_propagates_cancellation_to_it()
    {
        using var app = await FixtureApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        CancellationToken? signal = null;
        await using var session = await StartAsync(
            app,
            new WebConnectOptions
            {
                CdpEndpoint = _ => Task.FromResult(remote.Endpoint),
                ReconnectEndpoint = abort => { signal = abort; return new TaskCompletionSource<string>().Task; },
            },
            TimeSpan.FromSeconds(2));
        await WebEngine.SurfaceOf(session)!.Context().Browser!.CloseAsync();
        Assert.Equal("OPERATION_TIMEOUT", (await Assert.ThrowsAsync<EngineException>(() => session.ObserveAsync(None))).Code);
        Assert.True(signal?.IsCancellationRequested);
    }

    [Fact]
    public async Task Provisions_a_fresh_browser_for_each_attempt_and_refuses_accidental_reuse()
    {
        using var app = await FixtureApp.StartAsync();
        var hosts = new List<RemoteChrome>();
        var provisioned = 0;
        var reuse = false;
        var engine = new WebEngine(new WebEngineOptions
        {
            Connect = new WebConnectOptions
            {
                CdpEndpoint = async _ =>
                {
                    provisioned++;
                    if (!reuse)
                    {
                        hosts.Add(await RemoteChrome.LaunchAsync());
                    }

                    return hosts[^1].Endpoint;
                },
                ReconnectEndpoint = _ => Task.FromResult(hosts[^1].Endpoint),
            },
        });
        try
        {
            await using (var first = await StartAsync(app, engine))
            {
                await EvaluateAsync<JsonElement?>(first, "() => { localStorage.setItem('attempt', 'a1'); }");
            }

            await using (var second = await StartAsync(app, engine))
            {
                Assert.Null(await EvaluateAsync<string?>(second, "() => localStorage.getItem('attempt')"));
                Assert.Equal(2, provisioned);
            }

            reuse = true;
            var error = await Assert.ThrowsAsync<EngineException>(() => StartAsync(app, engine));
            Assert.Contains("reused a browser", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            foreach (var host in hosts)
            {
                await host.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Shares_recovery_between_concurrent_operations_while_a_cancelled_waiter_dispatches_nothing()
    {
        using var app = await FixtureApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        var endpoint = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconnected = 0;
        await using var session = await StartAsync(app, new WebConnectOptions
        {
            CdpEndpoint = _ => Task.FromResult(remote.Endpoint),
            ReconnectEndpoint = _ =>
            {
                reconnected++;
                requested.TrySetResult();
                return endpoint.Task;
            },
        });
        await WebEngine.SurfaceOf(session)!.Context().Browser!.CloseAsync();
        var first = EvaluateAsync<string>(session, "() => document.querySelector('h1').textContent");
        await requested.Task;
        using var controller = new CancellationTokenSource();
        var cancelled = session.OpenAsync(app.Url + "form", controller.Token);
        await controller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
        endpoint.SetResult(remote.Endpoint);
        Assert.Equal("Login", await first);
        Assert.Equal(app.Url + "login", WebEngine.SurfaceOf(session)!.Page().Url);
        Assert.Equal(1, reconnected);
    }

    [Fact]
    public async Task Counts_reconnect_time_against_the_operation_budget()
    {
        using var app = await FixtureApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        await using var session = await StartAsync(
            app,
            new WebConnectOptions
            {
                CdpEndpoint = _ => Task.FromResult(remote.Endpoint),
                ReconnectEndpoint = async _ =>
                {
                    await Task.Delay(300);
                    return remote.Endpoint;
                },
            },
            TimeSpan.FromSeconds(1));
        await WebEngine.SurfaceOf(session)!.Context().Browser!.CloseAsync();
        var error = await Assert.ThrowsAsync<EngineException>(() => EvaluateAsync<string>(session, """
            async () => {
              document.body.dataset['started'] = 'true';
              await new Promise((resolve) => setTimeout(resolve, 800));
              return 'too late';
            }
            """));
        Assert.Equal("OPERATION_TIMEOUT", error.Code);
        Assert.Equal("true", await WebEngine.SurfaceOf(session)!.Page().GetAttributeAsync("body", "data-started"));
    }

    [Fact]
    public async Task Allows_direct_test_code_mouse_input_using_current_geometry_while_observation_derived_taps_stay_stale()
    {
        using var app = await FixtureApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        var reconnected = 0;
        await using var session = await StartAsync(app, new WebConnectOptions
        {
            CdpEndpoint = _ => Task.FromResult(remote.Endpoint),
            ReconnectEndpoint = _ => { reconnected++; return Task.FromResult(remote.Endpoint); },
        });
        var browser = (IBrowserSession)session;
        var original = WebEngine.SurfaceOf(session)!.Page();
        await original.EvaluateAsync("""
            () => {
              const button = document.createElement('button');
              button.id = 'pointer-target';
              button.textContent = 'Count';
              button.style.cssText = 'position:fixed;left:20px;top:200px;width:120px;height:50px';
              button.addEventListener('click', () => {
                document.body.dataset['clicks'] = String(Number(document.body.dataset['clicks'] ?? 0) + 1);
              });
              document.addEventListener('wheel', (event) => {
                document.body.dataset['wheel'] = String(event.deltaY);
                event.preventDefault();
              }, { passive: false });
              document.body.append(button);
            }
            """);
        await session.ObserveAsync(None);
        await original.Locator("#pointer-target").EvaluateAsync("button => { button.style.left = '260px'; }");
        await original.Context.Browser!.CloseAsync();

        var point = (await EvaluateAsync<JsonElement>(session, """
            () => {
              const bounds = document.querySelector('#pointer-target').getBoundingClientRect();
              return { x: bounds.left + bounds.width / 2, y: bounds.top + bounds.height / 2 };
            }
            """))!;
        var x = point.GetProperty("x").GetSingle();
        var y = point.GetProperty("y").GetSingle();
        Assert.Equal(320, x);
        Assert.Equal(1, reconnected);
        await browser.MouseMoveAsync(x, y, None);
        await browser.MouseDownAsync(None);
        await browser.MouseUpAsync(None);
        await browser.MouseWheelAsync(0, 25, None);
        var wheel = await PollAsync(() => EvaluateAsync<string?>(session, "() => document.body.dataset['wheel'] ?? null"), "25");
        Assert.Equal("25", wheel);
        Assert.Equal("1", await EvaluateAsync<string?>(session, "() => document.body.dataset['clicks'] ?? null"));
        await session.ObserveAsync(None);
    }

    [Fact]
    public async Task Requires_new_evidence_for_focused_engine_keyboard_input_after_recovery_while_direct_test_input_remains_available()
    {
        using var app = await FixtureApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        await using var session = await StartAsync(app, Fixed(remote));
        var browser = (IBrowserSession)session;
        var page = WebEngine.SurfaceOf(session)!.Page();
        await page.EvaluateAsync("""
            () => {
              const field = document.createElement('input');
              field.id = 'keyboard-target';
              field.value = 'original';
              field.addEventListener('keydown', (event) => {
                if (event.key === 'Enter') document.body.dataset['enters'] = String(Number(document.body.dataset['enters'] ?? 0) + 1);
              });
              document.body.append(field);
              field.focus();
              field.setSelectionRange(field.value.length, field.value.length);
            }
            """);
        await session.ObserveAsync(None);
        await page.Context.Browser!.CloseAsync();
        Assert.Equal("NODE_STALE", (await Assert.ThrowsAsync<EngineException>(() => session.PressAsync("Enter", None))).Code);
        Task<string?> Value() => EvaluateAsync<string?>(session, "() => document.querySelector('#keyboard-target').value");
        Task<string?> Enters() => EvaluateAsync<string?>(session, "() => document.body.dataset['enters'] ?? '0'");
        Assert.Equal("original", await Value());
        Assert.Equal("0", await Enters());
        await browser.KeyboardTypeAsync(" direct", None);
        await browser.KeyboardPressAsync("Enter", None);
        Assert.Equal("original direct", await Value());
        Assert.Equal("1", await Enters());
        Assert.Equal("NODE_STALE", (await Assert.ThrowsAsync<EngineException>(() => session.PressAsync("Enter", None))).Code);
        await session.ObserveAsync(None);
        await session.PressAsync("Enter", None);
        Assert.Equal("2", await Enters());
    }

    [Fact]
    public async Task Refuses_recovery_when_disconnected_navigation_lost_the_closed_root_tracking_hook()
    {
        using var app = await FixtureApp.StartAsync();
        await using var remote = await RemoteChrome.LaunchAsync();
        await using var session = await StartAsync(app, Fixed(remote));
        await WebEngine.SurfaceOf(session)!.Context().Browser!.CloseAsync();
        using (var playwright = await Playwright.CreateAsync())
        {
            var hostControl = await playwright.Chromium.ConnectOverCDPAsync(remote.Endpoint);
            try
            {
                var page = hostControl.Contexts[0].Pages.First(candidate => candidate.Url.EndsWith("/login", StringComparison.Ordinal));
                await page.GotoAsync(app.Url + "form");
                var tracked = await page.EvaluateAsync<bool>("""
                    () => {
                      const shadowHost = document.createElement('div');
                      document.body.append(shadowHost);
                      const root = shadowHost.attachShadow({ mode: 'closed' });
                      root.innerHTML = '<input type="password" value="private">';
                      return Object.prototype.hasOwnProperty.call(globalThis, Symbol.for('e2e.closedShadowRoots'));
                    }
                    """);
                Assert.False(tracked);
                Assert.Equal(1, await page.Locator("body").CountAsync());
            }
            finally
            {
                await hostControl.CloseAsync();
            }
        }

        Assert.Equal("ENGINE_FAILURE", (await Assert.ThrowsAsync<EngineException>(() => session.ObserveAsync(None))).Code);
    }

    private static WebConnectOptions Fixed(RemoteChrome remote) => new()
    {
        CdpEndpoint = _ => Task.FromResult(remote.Endpoint),
        ReconnectEndpoint = _ => Task.FromResult(remote.Endpoint),
    };

    /// <summary>Starts the engine's attempt against a host-provisioned browser and opens <c>/login</c>.</summary>
    private static Task<IEngineSession> StartAsync(TinySite app, WebConnectOptions connect, TimeSpan? actionTimeout = null)
    {
        return StartAsync(app, new WebEngine(new WebEngineOptions { Connect = connect }), actionTimeout);
    }

    private static async Task<IEngineSession> StartAsync(TinySite app, WebEngine engine, TimeSpan? actionTimeout = null)
    {
        var session = await engine.StartAsync(new EngineStartOptions { BaseUrl = app.Url, ActionTimeout = actionTimeout ?? TimeSpan.FromSeconds(10) }, None);
        try
        {
            await session.OpenAsync(app.Url + "login", None);
            return session;
        }
        catch
        {
            await session.DisposeAsync();
            throw;
        }
    }

    private static async Task<T?> EvaluateAsync<T>(IEngineSession session, string expression)
    {
        var result = await ((IBrowserSession)session).EvaluateAsync(expression, null, hasArg: false, None);
        return result is { } element ? element.Deserialize<T>() : default;
    }

    private static async Task<string?> PollAsync(Func<Task<string?>> read, string expected)
    {
        var value = await read();
        for (var tries = 0; value != expected && tries < 50; tries++)
        {
            await Task.Delay(100);
            value = await read();
        }

        return value;
    }

    private static SemanticNode Find(Observation observation, Func<SemanticNode, bool> predicate) =>
        Flatten(observation.Roots).First(predicate);

    private static IEnumerable<SemanticNode> Flatten(IEnumerable<SemanticNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
            {
                yield return child;
            }
        }
    }
}
