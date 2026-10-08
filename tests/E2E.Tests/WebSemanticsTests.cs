// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text;
using E2E;
using E2E.Engine;
using E2E.Internal;

namespace E2E.Tests;

[Collection(BrowserCollection.Name)]
public sealed class WebSemanticsTests
{
    private const string RolesPage = """
        <!DOCTYPE html>
        <html><body>
        <header>Top</header>
        <nav><a href="/a">A</a></nav>
        <main>
          <section aria-label="Plans"><p>Pick one</p></section>
          <section><p>Unnamed</p></section>
          <form aria-label="Signup"><input placeholder="Email"></form>
          <form><input placeholder="Plain"></form>
          <ul><li>One</li></ul>
          <table><tbody><tr><th>Plan</th><td>Pro</td></tr></tbody></table>
          <img alt="" src="data:image/gif;base64,R0lGODlhAQABAAAAACw=">
          <img alt="Logo" src="data:image/gif;base64,R0lGODlhAQABAAAAACw=">
          <select aria-label="Size"><option>S</option><option selected>M</option></select>
          <select aria-label="Tags" multiple><option>x</option><option>y</option></select>
          <dialog open>Hello</dialog>
          <hr>
          <progress value="1" max="2"></progress>
          <input type="range" aria-label="Volume">
          <input type="number" aria-label="Qty">
          <button aria-expanded="true">Menu</button>
          <button aria-pressed="true">Bold</button>
          <div role="tablist"><div role="tab" aria-selected="true">General</div></div>
          <input aria-label="Focus me">
          <script>document.querySelector("[aria-label='Focus me']").focus();</script>
          <div data-qa="custom">Custom id</div>
        </main>
        <footer>Bottom</footer>
        </body></html>
        """;

    private const string ShadowPage = """
        <!DOCTYPE html>
        <html><body>
        <open-card></open-card>
        <closed-card></closed-card>
        <div role="status"></div>
        <script>
          const say = (text) => () => { document.querySelector("[role=status]").textContent = text; };
          const open = document.querySelector("open-card").attachShadow({ mode: "open" });
          open.innerHTML = "<button>Open inside</button>";
          open.querySelector("button").addEventListener("click", say("open clicked"));
          const closed = document.querySelector("closed-card").attachShadow({ mode: "closed" });
          closed.innerHTML = "<button>Closed inside</button>";
          closed.querySelector("button").addEventListener("click", say("closed clicked"));
        </script>
        </body></html>
        """;

    private const string FramePage = """
        <!DOCTYPE html>
        <html><body>
        <h1>Host</h1>
        <iframe title="Checkout" srcdoc="<button onclick=&quot;document.querySelector('output').textContent='Paid'&quot;>Pay</button><output></output><iframe title='Nested' srcdoc='<a href=/x>Deep</a>'></iframe>"></iframe>
        <iframe hidden srcdoc="<button>Hidden frame</button>"></iframe>
        </body></html>
        """;

    [Fact]
    public async Task Chromium_reports_landmark_structure_and_state_roles()
    {
        using var site = await TinySite.StartAsync(RolesPage);
        await using var session = await OpenAsync(site.Url, new WebEngineOptions { Headless = true, TestIdAttribute = "data-qa" });
        var observation = await session.ObserveAsync(CancellationToken.None);
        var nodes = Flatten(observation.Roots).ToList();
        var roles = nodes.Select(node => node.Role).ToHashSet();
        foreach (var role in new[] { "banner", "navigation", "main", "region", "form", "list", "listitem", "table", "rowgroup", "row", "columnheader", "cell", "image", "combobox", "listbox", "option", "dialog", "separator", "progressbar", "slider", "spinbutton", "tab", "contentinfo" })
        {
            Assert.Contains(role, roles);
        }

        Assert.DoesNotContain("presentation", roles);
        Assert.Single(nodes, node => node.Role == "region");
        Assert.Single(nodes, node => node.Role == "form");
        Assert.Single(nodes, node => node.Role == "image");
        var size = Assert.Single(nodes, node => node.Role == "combobox");
        Assert.Equal(["S", "M"], size.Children.Select(child => child.Name));
        Assert.True(size.Children[1].States.Selected);
        Assert.False(size.Children[0].States.Selected);
        Assert.All(size.Children, child => Assert.False(child.States.Hidden));
        Assert.True(nodes.Single(node => node.Name == "Menu").States.Expanded);
        Assert.True(nodes.Single(node => node.Name == "Bold").States.Pressed);
        Assert.True(nodes.Single(node => node.Role == "tab").States.Selected);
        Assert.True(nodes.Single(node => node.Name == "Focus me").States.Focused);
        Assert.False(nodes.Single(node => node.Name == "Menu").States.Focused);
        Assert.Equal("custom", Assert.Single(nodes, node => node.TestId is not null).TestId);
        Assert.False(observation.Truncated);
    }

    [Fact]
    public async Task Chromium_walks_open_and_closed_shadow_roots_and_acts_inside_them()
    {
        using var site = await TinySite.StartAsync(ShadowPage);
        var session = await StartSessionAsync(site.Url);
        await RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            await session.Screen.GetByRole("button", "Open inside").TapAsync();
            await Expect.That(session.Screen.GetByRole("status")).ToContainTextAsync("open clicked");
            await session.Screen.GetByRole("button", "Closed inside").TapAsync();
            await Expect.That(session.Screen.GetByRole("status")).ToContainTextAsync("closed clicked");
        });
    }

    [Fact]
    public async Task Chromium_stitches_iframe_documents_under_their_frame_node()
    {
        using var site = await TinySite.StartAsync(FramePage);
        await using (var engine = await OpenAsync(site.Url, new WebEngineOptions { Headless = true }))
        {
            var observation = await engine.ObserveAsync(CancellationToken.None);
            var frame = Assert.Single(observation.Roots, node => node.Role == "iframe");
            Assert.Equal("Checkout", frame.Name);
            var inside = Flatten(frame.Children).ToList();
            Assert.Contains(inside, node => node.Role == "button" && node.Name == "Pay");
            var nested = Assert.Single(inside, node => node.Role == "iframe");
            Assert.Contains(Flatten(nested.Children), node => node.Role == "link" && node.Name == "Deep");
            Assert.DoesNotContain(Flatten(observation.Roots), node => node.Name == "Hidden frame");
            var refs = Flatten(observation.Roots).Select(node => node.Ref).ToList();
            Assert.Equal(refs.Count, refs.Distinct().Count());
        }

        var session = await StartSessionAsync(site.Url);
        await RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            await session.Screen.GetByRole("button", "Pay").TapAsync();
            await Expect.That(session.Screen.GetByRole("status")).ToContainTextAsync("Paid");
        });
    }

    [Fact]
    public async Task Chromium_stops_at_the_node_budget_and_says_so()
    {
        var html = new StringBuilder("<!DOCTYPE html><html><body>");
        for (var index = 0; index < ObservationLimits.Nodes + 50; index++)
        {
            html.Append("<button>b</button>");
        }

        html.Append("</body></html>");
        using var site = await TinySite.StartAsync(html.ToString());
        await using var session = await OpenAsync(site.Url, new WebEngineOptions { Headless = true });
        var observation = await session.ObserveAsync(CancellationToken.None);
        Assert.True(observation.Truncated);
        Assert.Equal(ObservationLimits.Nodes, observation.Roots.Count);
    }

    [Fact]
    public async Task Chromium_applies_viewport_user_agent_headers_and_basic_auth()
    {
        using var site = await TinySite.StartAsync(async context =>
        {
            var request = context.Request;
            if (request.Headers["Authorization"] is null)
            {
                context.Response.StatusCode = 401;
                context.Response.AddHeader("WWW-Authenticate", "Basic realm=\"staging\"");
                context.Response.Close();
                return;
            }

            var auth = Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers["Authorization"]!["Basic ".Length..]));
            await TinySite.RespondAsync(context, $$"""
                <!DOCTYPE html><html><body>
                <p data-testid="auth">{{auth}}</p>
                <p data-testid="header">{{request.Headers["x-preview"]}}</p>
                <p data-testid="ua"></p>
                <p data-testid="size"></p>
                <script>
                  document.querySelector("[data-testid=ua]").textContent = navigator.userAgent;
                  document.querySelector("[data-testid=size]").textContent = innerWidth + "x" + innerHeight;
                </script>
                </body></html>
                """);
        });
        await using var session = await OpenAsync(site.Url, new WebEngineOptions
        {
            Headless = true,
            Viewport = new WebViewport(800, 600),
            UserAgent = "e2e-test-agent",
            Headers = new Dictionary<string, string> { ["X-Preview"] = "bypass" },
            BasicAuth = new WebBasicAuth("ada", Secret.Create("pw", "s3cret")),
        });
        var nodes = Flatten((await session.ObserveAsync(CancellationToken.None)).Roots).ToList();
        string TextOf(string testId) => nodes.Single(node => node.TestId == testId).Text ?? "";
        Assert.Equal("ada:s3cret", TextOf("auth"));
        Assert.Equal("bypass", TextOf("header"));
        Assert.Equal("e2e-test-agent", TextOf("ua"));
        Assert.Equal("800x600", TextOf("size"));
    }

    [Fact]
    public async Task Chromium_waits_for_the_load_event_when_it_opens_a_page()
    {
        using var site = await TinySite.StartAsync(async context =>
        {
            if (context.Request.Url!.AbsolutePath == "/slow.css")
            {
                await Task.Delay(700);
                var css = Encoding.UTF8.GetBytes("p{}");
                context.Response.ContentType = "text/css";
                await context.Response.OutputStream.WriteAsync(css);
                context.Response.Close();
                return;
            }

            await TinySite.RespondAsync(context, """
                <!DOCTYPE html><html><body>
                <div role="status">loading</div>
                <script>
                  const link = document.createElement("link");
                  link.rel = "stylesheet";
                  link.href = "/slow.css";
                  document.head.appendChild(link);
                  addEventListener("load", () => { document.querySelector("[role=status]").textContent = "loaded"; });
                </script>
                </body></html>
                """);
        });
        await using var session = await OpenAsync(site.Url, new WebEngineOptions { Headless = true });
        var observation = await session.ObserveAsync(CancellationToken.None);
        Assert.Equal("loaded", Assert.Single(observation.Roots, node => node.Role == "status").Name);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5000/path?q=1", true)]
    [InlineData("https://127.0.0.1/other", true)]
    [InlineData("http://localhost:5000/", false)]
    [InlineData("https://cdn.example.com/app.js", false)]
    [InlineData("data:text/plain,hi", false)]
    public void Headers_ride_only_requests_for_the_app_host(string url, bool expected)
    {
        Assert.Equal(expected, WebEngine.IsAppRequest(url, new Uri("http://127.0.0.1:5000/")));
    }

    [Fact]
    public void Invalid_web_options_are_refused()
    {
        Assert.Equal("INVALID_CONFIG", Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions { TestIdAttribute = " " })).Code);
        Assert.Equal("INVALID_CONFIG", Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions { Viewport = new WebViewport(0, 10) })).Code);
        Assert.Equal("INVALID_CONFIG", Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions { BasicAuth = new WebBasicAuth("a:b", "c") })).Code);
        Assert.Equal("INVALID_CONFIG", Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions
        {
            UserAgent = "x",
            Headers = new Dictionary<string, string> { ["User-Agent"] = "y" },
        })).Code);
    }

    [Fact]
    public void Snapshot_text_shows_states_and_truncation()
    {
        var observation = new Observation
        {
            Route = "/",
            Truncated = true,
            Roots =
            [
                new SemanticNode
                {
                    Ref = "e1",
                    Role = "button",
                    Name = "Menu",
                    States = new NodeStates { Expanded = true, Pressed = true, Focused = true },
                },
                new SemanticNode { Ref = "e2", Role = "tab", Name = "General", States = new NodeStates { Selected = true } },
            ],
        };

        var text = SnapshotText.Render(observation, Redactor.None);
        Assert.Contains("button \"Menu\" [ref=e1] [expanded] [pressed] [focused]", text, StringComparison.Ordinal);
        Assert.Contains("tab \"General\" [ref=e2] [selected]", text, StringComparison.Ordinal);
        Assert.Contains("More of the page is off screen", text, StringComparison.Ordinal);
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

    internal static async Task<IEngineSession> OpenAsync(string url, WebEngineOptions options)
    {
        var session = await new WebEngine(options).StartAsync(
            new EngineStartOptions { BaseUrl = url, ActionTimeout = TimeSpan.FromSeconds(10) },
            CancellationToken.None);

        await session.OpenAsync(url, CancellationToken.None);
        return session;
    }

    private static async Task<E2ESession> StartSessionAsync(string url)
    {
        return await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            BaseUrl = url,
            TestTitle = "web > semantics",
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
