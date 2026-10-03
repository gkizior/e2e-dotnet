// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using E2E;
using E2E.Engine;

namespace E2E.Tests;

[Collection(BrowserCollection.Name)]
public sealed class WebEngineTests
{
    private const string BillingPage = """
        <!DOCTYPE html>
        <html><body>
        <h1>Billing</h1>
        <button type="button">Upgrade to Pro</button>
        <p id="invoice" hidden>Prorated amount: $12</p>
        <div role="status" hidden>Pro</div>
        <script>
          document.querySelector("button").addEventListener("click", () => {
            document.getElementById("invoice").hidden = false;
            document.querySelector("[role=status]").hidden = false;
          });
        </script>
        </body></html>
        """;

    private const string ProjectsPage = """
        <!DOCTYPE html>
        <html><body>
        <ul>
          <li>Alpha <button type="button" onclick="removed('Alpha')">Delete</button></li>
          <li>Beta <button type="button" onclick="removed('Beta')">Delete</button></li>
        </ul>
        <div role="status"></div>
        <script>
          function removed(name) { document.querySelector("[role=status]").textContent = "Deleted " + name; }
        </script>
        </body></html>
        """;

    [Fact]
    public async Task Chromium_upgrades_the_plan_when_a_browser_is_installed()
    {
        using var site = await TinySite.StartAsync(BillingPage);
        var session = await StartAsync(site, UpgradeModel(() => { }), cache: null);
        await RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            await session.Agent.ActAsync("upgrade the workspace to the Pro plan");
            await Expect.That(session.Screen.GetByRole("status")).ToContainTextAsync("Pro");
        });
    }

    [Fact]
    public async Task Chromium_acts_on_the_element_a_ref_names_when_two_share_a_role_and_name()
    {
        using var site = await TinySite.StartAsync(ProjectsPage);
        var model = new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            if (text.Contains("tapped", StringComparison.Ordinal))
            {
                return ModelResponses.Done("passed", "deleted");
            }

            var target = Regex.Matches(text, @"button ""Delete"" \[ref=(e\d+)\]")[^1].Groups[1].Value;
            return ModelResponses.Call("tap", new { @ref = target });
        });
        var session = await StartAsync(site, model, cache: null);
        await RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            await session.Agent.ActAsync("delete the Beta project");
            await Expect.That(session.Screen.GetByRole("status")).ToContainTextAsync("Deleted Beta");
            await session.Screen.GetByRole("button", "Delete").First().TapAsync();
            await Expect.That(session.Screen.GetByRole("status")).ToContainTextAsync("Deleted Alpha");
        });
    }

    [Fact]
    public async Task Chromium_replays_a_recorded_act_without_the_model()
    {
        using var site = await TinySite.StartAsync(BillingPage);
        var directory = Path.Combine(Path.GetTempPath(), "e2e-web", Guid.NewGuid().ToString("n"));
        for (var run = 0; run < 2; run++)
        {
            var calls = 0;
            var session = await StartAsync(site, UpgradeModel(() => calls++), new FileStepCache(directory));
            await RunAsync(session, async () =>
            {
                await session.App.OpenAsync("/");
                await session.Agent.ActAsync("upgrade the workspace to the Pro plan");
                await Expect.That(session.Screen.GetByRole("status")).ToContainTextAsync("Pro");
            });
            Assert.Equal(run, session.Replayed);
            Assert.Equal(run == 0, calls > 0);
        }
    }

    [Fact]
    public async Task Chromium_reads_names_and_states_the_way_accname_and_aria_do()
    {
        const string page = """
            <!DOCTYPE html>
            <html><body>
            <span id="heading">Referenced name</span>
            <button type="button" data-testid="labelled" aria-labelledby="heading" aria-label="Own label">x</button>
            <button type="button" data-testid="aria-disabled" aria-disabled="true">Save</button>
            <div aria-disabled="true">
              <button type="button" data-testid="inherited">Inherited</button>
              <div aria-disabled="false"><button type="button" data-testid="cut">Cut</button></div>
            </div>
            <fieldset disabled>
              <legend><input type="checkbox" data-testid="legend"></legend>
              <input type="text" data-testid="fieldset">
            </fieldset>
            <button type="button" data-testid="enabled">Enabled</button>
            <div role="checkbox" aria-checked="true" data-testid="custom-checked">Custom</div>
            <div role="switch" aria-checked="true" data-testid="switch">Switch</div>
            <div role="checkbox" aria-checked="mixed" data-testid="mixed">Mixed</div>
            <input type="checkbox" aria-checked="true" data-testid="native">
            </body></html>
            """;
        using var site = await TinySite.StartAsync(page);
        var session = await new WebEngine(headless: true).StartAsync(new EngineStartOptions { BaseUrl = site.Url }, CancellationToken.None);

        await using (session)
        {
            await session.OpenAsync(site.Url, CancellationToken.None);
            var observation = await session.ObserveAsync(CancellationToken.None);
            var nodes = Flatten(observation.Roots).Where(node => node.TestId is not null).ToDictionary(node => node.TestId!);

            Assert.Equal("Referenced name", nodes["labelled"].Name);
            Assert.True(nodes["aria-disabled"].States.Disabled);
            Assert.True(nodes["inherited"].States.Disabled);
            Assert.False(nodes["cut"].States.Disabled);
            Assert.True(nodes["fieldset"].States.Disabled);
            Assert.False(nodes["legend"].States.Disabled);
            Assert.False(nodes["enabled"].States.Disabled);
            Assert.True(nodes["custom-checked"].States.Checked);
            Assert.True(nodes["switch"].States.Checked);
            Assert.False(nodes["mixed"].States.Checked);
            Assert.False(nodes["native"].States.Checked);
        }
    }

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

    [Fact]
    public async Task Chromium_browser_fixture_navigates_reads_and_drives_input()
    {
        using var site = await TinySite.StartAsync(InputPage);
        var session = await StartAsync(site, UpgradeModel(() => { }), cache: null);
        await RunAsync(session, async () =>
        {
            var browser = session.Browser;
            Assert.Equal("web", session.Context.Platform);
            await session.App.OpenAsync("/first");
            await session.App.OpenAsync("/second");
            Assert.Equal(site.Url + "second", await browser.UrlAsync());
            Assert.Equal("Input page", await browser.TitleAsync());

            await browser.BackAsync();
            await browser.WaitForURLAsync("/first");
            await browser.ForwardAsync();
            await browser.WaitForURLAsync(new Regex("/second$"));
            await session.App.BackAsync();
            await browser.WaitForURLAsync("/first");
            var missed = await Assert.ThrowsAsync<TestException>(() => browser.WaitForURLAsync("/never", TimeSpan.FromMilliseconds(200)));
            Assert.Equal("ASSERTION_FAILED", missed.Code);

            await browser.EvaluateAsync<object>("() => { window.marker = 1; return null; }");
            await browser.ReloadAsync();
            Assert.Equal(0, await browser.EvaluateAsync<int>("() => window.marker ?? 0"));
            Assert.Equal(42, await browser.EvaluateAsync<int>("x => x * 2", 21));
            var thrown = await Assert.ThrowsAsync<TestException>(() => browser.EvaluateAsync<int>("() => { throw new Error('boom'); }"));
            Assert.Equal("EVALUATE_FAILED", thrown.Code);

            await browser.SetViewportAsync(800, 600);
            Assert.Equal(800, await browser.EvaluateAsync<int>("() => window.innerWidth"));

            await browser.Mouse.MoveAsync(20, 20);
            await browser.Mouse.DownAsync();
            await browser.Mouse.UpAsync();
            await browser.Keyboard.TypeAsync("hello");
            await browser.Keyboard.PressAsync("!");
            Assert.Equal("hello!", await browser.EvaluateAsync<string>("() => document.querySelector('input').value"));
        });
    }

    [Fact]
    public async Task Chromium_restart_keeps_cookies_and_clear_state_drops_them()
    {
        using var site = await TinySite.StartAsync(InputPage);
        var session = await StartAsync(site, UpgradeModel(() => { }), cache: null);
        await RunAsync(session, async () =>
        {
            var browser = session.Browser;
            Assert.Equal(site.Url, session.App.BaseUrl);
            await session.App.OpenAsync("/");
            await browser.SetCookiesAsync([new BrowserCookie { Name = "plan", Value = "pro", Url = site.Url }]);
            var denied = await Assert.ThrowsAsync<TestException>(() => browser.SetCookiesAsync([new BrowserCookie { Name = "x", Value = "y" }]));
            Assert.Equal("INVALID_ARGUMENT", denied.Code);
            await browser.SetViewportAsync(700, 500);

            await session.App.RestartAsync();
            Assert.Equal("about:blank", await browser.UrlAsync());
            Assert.Contains(await browser.CookiesAsync(), cookie => cookie.Name == "plan" && cookie.Value == "pro");
            await session.App.OpenAsync("/");
            Assert.Equal(700, await browser.EvaluateAsync<int>("() => window.innerWidth"));

            await session.App.ClearStateAsync();
            Assert.Equal("about:blank", await browser.UrlAsync());
            Assert.Empty(await browser.CookiesAsync());
            await session.App.OpenAsync("/");
            await Expect.That(session.Screen.GetByRole("textbox")).ToBeVisibleAsync();
        });
    }

    private const string InputPage = """
        <!DOCTYPE html>
        <html><head><title>Input page</title></head><body style="margin:0">
        <input style="position:absolute;left:0;top:0;width:200px;height:40px">
        </body></html>
        """;

    [Fact]
    public async Task Chromium_runs_the_extra_locator_actions()
    {
        const string page = """
            <!DOCTYPE html>
            <html><body>
            <button type="button" ondblclick="log('double')" onclick="log('click')">Like</button>
            <input aria-label="Search" onkeydown="log('key ' + event.key)">
            <input aria-label="Password" type="password" oninput="log('pw ' + this.value.length)">
            <select aria-label="Plan" onchange="log('plan ' + this.value)">
              <option value="free">Free</option>
              <option value="pro">Pro</option>
            </select>
            <div style="height: 3000px"></div>
            <button type="button" onfocus="log('focus')">Far</button>
            <div role="status"></div>
            <script>
              function log(entry) { document.querySelector("[role=status]").textContent += entry + ";"; }
            </script>
            </body></html>
            """;
        using var site = await TinySite.StartAsync(page);
        var session = await StartAsync(site, UpgradeModel(() => { }), cache: null);
        await RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            var screen = session.Screen;
            await screen.GetByRole("button", "Like").ClickAsync();
            await screen.GetByRole("button", "Like").DoubleTapAsync();
            await screen.GetByRole("textbox", "Search").PressSequentiallyAsync("ab");
            await screen.GetByRole("textbox", "Password").FillAsync(Secret.Create("password", "hunter2"));
            await screen.GetByRole("combobox", "Plan").SelectOptionAsync("pro");
            await screen.GetByRole("button", "Far").ScrollIntoViewAsync();
            await screen.GetByRole("button", "Far").FocusAsync();
            await Expect.That(screen.GetByRole("status")).ToContainTextAsync("click;click;click;double;key a;key b;pw 7;plan pro;focus;");
            Assert.Equal("ab", await screen.GetByRole("textbox", "Search").InputValueAsync());
        });
    }

    [Fact]
    public async Task Chromium_reads_attributes_boxes_and_waits_for_states()
    {
        using var site = await TinySite.StartAsync(BillingPage);
        var session = await StartAsync(site, UpgradeModel(() => { }), cache: null);
        await RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            var upgrade = session.Screen.GetByRole("button", "Upgrade to Pro");
            Assert.Equal("button", await upgrade.GetAttributeAsync("type"));
            var box = await upgrade.BoundingBoxAsync();
            Assert.NotNull(box);
            Assert.True(box.Width > 0 && box.Height > 0);

            var status = session.Screen.GetByRole("status");
            Assert.True(await status.IsHiddenAsync());
            await status.WaitForAsync(new LocatorWaitForOptions { State = WaitForState.Attached });
            await upgrade.TapAsync(new ActionOptions { Timeout = TimeSpan.FromSeconds(2) });
            await status.WaitForAsync();
            Assert.True(await status.IsVisibleAsync());
        });
    }

    [Fact]
    public async Task Chromium_reports_role_states_image_alias_and_hidden_ancestors()
    {
        const string page = """
            <!DOCTYPE html>
            <html><body>
            <div role="tablist"><div role="tab" aria-selected="true">One</div><div role="tab" aria-selected="false">Two</div></div>
            <button type="button" aria-expanded="true">Menu</button>
            <button type="button" aria-pressed="true">Bold</button>
            <div role="img" aria-label="Logo"></div>
            <div style="display: none"><p>Saved</p></div>
            <p>Saved</p>
            </body></html>
            """;
        using var site = await TinySite.StartAsync(page);
        var session = await StartAsync(site, new ScriptedModel(_ => ModelResponses.Done("passed", "none")), cache: null);
        await RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            await Expect.That(session.Screen.GetByRole("tab", new RoleOptions { Selected = true })).ToHaveTextAsync("One");
            await Expect.That(session.Screen.GetByRole("button", new RoleOptions { Expanded = true })).ToHaveTextAsync("Menu");
            await Expect.That(session.Screen.GetByRole("button", new RoleOptions { Pressed = true })).ToHaveTextAsync("Bold");
            await Expect.That(session.Screen.GetByRole("img", "Logo")).ToBeVisibleAsync();
            await Expect.That(session.Screen.GetByText("Saved")).ToHaveCountAsync(2);
            await Expect.That(session.Screen.GetByText("Saved", new TextMatchOptions { Visible = true })).ToBeVisibleAsync();
        });
    }

    private static ScriptedModel UpgradeModel(Action onCall)
    {
        return new ScriptedModel(request =>
        {
            onCall();
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            if (text.Contains("tapped", StringComparison.Ordinal))
            {
                return ModelResponses.Done("passed", "upgraded");
            }

            return ModelResponses.Tap("button", "Upgrade to Pro");
        });
    }

    [Fact]
    public async Task Chromium_reports_selected_expanded_focused_and_attributes()
    {
        using var site = await TinySite.StartAsync("""
            <!DOCTYPE html>
            <html><body>
            <div role="tab" aria-selected="true">Overview</div>
            <button type="button" aria-expanded="true">Menu</button>
            <a href="/docs" target="_blank">Docs</a>
            <input aria-label="Email" autofocus>
            <script>document.querySelector("input").focus();</script>
            </body></html>
            """);
        var session = await StartAsync(site, new ScriptedModel(_ => ModelResponses.Done("passed", "unused")), cache: null);
        await RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            await Expect.That(session.Screen.GetByRole("tab", "Overview")).ToBeSelectedAsync();
            await Expect.That(session.Screen.GetByRole("button", "Menu")).ToBeExpandedAsync();
            await Expect.That(session.Screen.GetByLabel("Email")).ToBeFocusedAsync();
            await Expect.That(session.Screen.GetByRole("link", "Docs")).ToHaveAttributeAsync("target", "_blank");
        });
    }

    private static async Task<E2ESession> StartAsync(TinySite site, ScriptedModel model, FileStepCache? cache)
    {
        return await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = model,
            BaseUrl = site.Url,
            Cache = cache,
            CacheEnabled = cache is not null,
            TestTitle = "web > step",
            AssertionTimeout = TimeSpan.FromSeconds(5),
            ActionTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(20),
            TestTimeout = TimeSpan.FromSeconds(30),
        });
    }

    private static async Task RunAsync(E2ESession session, Func<Task> body)
    {
        await using (session)
        {
            Exception? error = null;
            try
            {
                await body();
            }
            catch (Exception ex)
            {
                error = ex;
            }

            session.Complete(error);
            Assert.Null(error);
        }
    }
}

internal sealed class TinySite : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Func<HttpListenerContext, Task> _handler;

    private TinySite(string url, Func<HttpListenerContext, Task> handler)
    {
        Url = url;
        _handler = handler;
    }

    public string Url { get; }

    public static Task<TinySite> StartAsync(string html)
    {
        return StartAsync(context => RespondAsync(context, html));
    }

    public static async Task RespondAsync(HttpListenerContext context, string html)
    {
        var page = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = page.Length;
        await context.Response.OutputStream.WriteAsync(page);
        context.Response.Close();
    }

    public static async Task<TinySite> StartAsync(Func<HttpListenerContext, Task> handler)
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var site = new TinySite("http://127.0.0.1:" + port.ToString(System.Globalization.CultureInfo.InvariantCulture) + "/", handler);
        site._listener.Prefixes.Add(site.Url);
        site._listener.Start();
        _ = Task.Run(() => site.ListenAsync(site._stop.Token));
        await Task.Delay(30);
        return site;
    }

    public void Dispose()
    {
        _stop.Cancel();
        _listener.Close();
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().WaitAsync(cancellationToken);
            }
            catch (Exception)
            {
                return;
            }

            _ = Task.Run(() => HandleAsync(context), CancellationToken.None);
        }
    }

    private async Task HandleAsync(HttpListenerContext context)
    {
        try
        {
            await _handler(context);
        }
        catch (Exception)
        {
            context.Response.Abort();
        }
    }
}
