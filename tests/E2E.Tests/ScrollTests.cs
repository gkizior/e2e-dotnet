// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using E2E.Engine;
using E2E.Internal;

namespace E2E.Tests;

[Collection(BrowserCollection.Name)]
public sealed class ScrollTests
{
    [Fact]
    public async Task Act_offers_observe_scroll_scroll_to_and_back_when_the_engine_can()
    {
        var model = Sequence(ModelResponses.Done("passed", "nothing to do"));
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/rows");
            await ctx.Agent.ActAsync("look around");
        }, Rows(), model);

        Assert.Null(result.Error);
        var names = model.Requests[0].Tools.Select(tool => tool.Name).ToList();
        Assert.Contains("observe", names);
        Assert.Contains("scroll", names);
        Assert.Contains("scroll_to", names);
        Assert.Contains("back", names);
    }

    [Fact]
    public void Scroll_and_back_need_their_engine_capability()
    {
        var names = AgentTools.ActFor(EngineCapabilities.Observation | EngineCapabilities.Actions).Select(tool => tool.Name).ToList();
        Assert.Contains("observe", names);
        Assert.DoesNotContain("scroll", names);
        Assert.DoesNotContain("scroll_to", names);
        Assert.DoesNotContain("back", names);
    }

    [Fact]
    public async Task Scroll_to_text_pages_until_the_row_shows_and_replays()
    {
        var directory = TempCache();
        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/rows");
            await ctx.Agent.ActAsync("open row 12");
            await Expect.That(ctx.Screen.GetByRole("status")).ToContainTextAsync("Opened Row 12");
        }

        var model = Sequence(
            ModelResponses.Call("scroll_to", new { text = "Row 12" }),
            ModelResponses.Tap("button", "Row 12"),
            ModelResponses.Done("passed", "opened"));
        var first = await RunAsync(Body, Rows(), model, directory);
        Assert.Null(first.Error);
        Assert.Contains("until \"Row 12\" was in view", ToolResults(model)[0], StringComparison.Ordinal);
        var entry = ReadEntry(directory);
        Assert.Equal(["scrollUntil", "tap"], entry.Actions.Select(action => action.Kind));
        Assert.Equal("Row 12", entry.Actions[0].Text);
        Assert.Equal("down", entry.Actions[0].Direction);

        var second = await RunAsync(Body, Rows(), Sequence(), directory);
        Assert.Null(second.Error);
        Assert.Equal(1, second.Replayed);
        Assert.Equal(0, second.ModelCalls);
    }

    [Fact]
    public async Task Consecutive_scrolls_fold_into_one_recorded_action_and_replay()
    {
        var directory = TempCache();
        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/rows");
            await ctx.Agent.ActAsync("open row 14");
            await Expect.That(ctx.Screen.GetByRole("status")).ToContainTextAsync("Opened Row 14");
        }

        var model = Sequence(
            ModelResponses.Call("scroll", new { direction = "down", times = 2 }),
            ModelResponses.Call("scroll", new { direction = "down" }),
            ModelResponses.Tap("button", "Row 14"),
            ModelResponses.Done("passed", "opened"));
        var first = await RunAsync(Body, Rows(), model, directory);
        Assert.Null(first.Error);
        Assert.StartsWith("scrolled down 2 screens", ToolResults(model)[0], StringComparison.Ordinal);
        var entry = ReadEntry(directory);
        Assert.Equal(["scroll", "tap"], entry.Actions.Select(action => action.Kind));
        Assert.Equal(3, entry.Actions[0].Times);
        Assert.Null(entry.Actions[0].Role);

        var second = await RunAsync(Body, Rows(), Sequence(), directory);
        Assert.Null(second.Error);
        Assert.Equal(1, second.Replayed);
    }

    [Fact]
    public async Task A_scroll_on_a_list_replays_on_the_re_found_list()
    {
        var directory = TempCache();
        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/rows");
            await ctx.Agent.ActAsync("open row 7");
            await Expect.That(ctx.Screen.GetByRole("status")).ToContainTextAsync("Opened Row 7");
        }

        var model = Sequence(
            ModelResponses.Call("scroll", new { role = "list", name = "Rows", direction = "down" }),
            ModelResponses.Tap("button", "Row 7"),
            ModelResponses.Done("passed", "opened"));
        var first = await RunAsync(Body, Rows(), model, directory);
        Assert.Null(first.Error);
        var entry = ReadEntry(directory);
        Assert.Equal("list", entry.Actions[0].Role);
        Assert.Equal("Rows", entry.Actions[0].Name);

        var second = await RunAsync(Body, Rows(), Sequence(), directory);
        Assert.Null(second.Error);
        Assert.Equal(1, second.Replayed);
    }

    [Fact]
    public async Task Scroll_to_text_that_never_shows_fails_the_tool_call()
    {
        var model = Sequence(
            ModelResponses.Call("scroll_to", new { text = "Row 99" }),
            ModelResponses.Done("failed", "There is no row 99."));
        var result = await RunAsync(async ctx =>
        {
            await ctx.App.OpenAsync("/rows");
            await ctx.Agent.ActAsync("open row 99");
        }, Rows(), model);

        Assert.IsType<AgentException>(result.Error);
        var output = ToolResults(model)[0];
        Assert.StartsWith("failed:", output, StringComparison.Ordinal);
        Assert.Contains("stopped moving", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Back_returns_to_the_previous_page_and_replays_while_observe_records_nothing()
    {
        var directory = TempCache();
        var world = new DocumentWorld()
            .Map("/start", page =>
            {
                var status = page.Status("Finished", hidden: true);
                page.Link("Details", "/details");
                page.Button("Finish", () => status.Hidden = false);
            })
            .Map("/details", page => page.Heading("Details"));
        async Task Body(TestContext ctx)
        {
            await ctx.App.OpenAsync("/start");
            await ctx.Agent.ActAsync("read the details, then finish");
            await Expect.That(ctx.Screen.GetByRole("status", "Finished")).ToBeVisibleAsync();
        }

        var model = Sequence(
            ModelResponses.Call("observe", new { }),
            ModelResponses.Tap("link", "Details"),
            ModelResponses.Call("back", new { }),
            ModelResponses.Tap("button", "Finish"),
            ModelResponses.Done("passed", "finished"));
        var first = await RunAsync(Body, world, model, directory);
        Assert.Null(first.Error);
        var results = ToolResults(model);
        Assert.StartsWith("observed\nScreen (/start)", results[0], StringComparison.Ordinal);
        Assert.StartsWith("navigated back\nScreen (/start)", results[2], StringComparison.Ordinal);
        var entry = ReadEntry(directory);
        Assert.Equal(["tap", "back", "tap"], entry.Actions.Select(action => action.Kind));

        var second = await RunAsync(Body, world, Sequence(), directory);
        Assert.Null(second.Error);
        Assert.Equal(1, second.Replayed);
    }

    [Fact]
    public void A_truncated_snapshot_tells_the_model_to_scroll()
    {
        var observation = new Observation
        {
            Route = "/",
            Roots = [new SemanticNode { Ref = "e1", Role = "button", Name = "Row 1" }],
            Truncated = true,
        };

        Assert.Contains("Scroll to reach it", SnapshotText.Render(observation, Redactor.None), StringComparison.Ordinal);
        Assert.DoesNotContain("Scroll", SnapshotText.Render(new Observation { Route = "/", Roots = observation.Roots }, Redactor.None), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chromium_scrolls_to_a_row_past_the_node_budget()
    {
        var html = new StringBuilder("<!DOCTYPE html><html><body><div role=\"status\">none</div><ul>");
        for (var row = 1; row <= ObservationLimits.Nodes + 200; row++)
        {
            var label = "Item " + row.ToString(CultureInfo.InvariantCulture);
            html.Append("<li><button type=\"button\" onclick=\"document.querySelector('[role=status]').textContent='Opened ").Append(label).Append("'\">")
                .Append(label).Append("</button></li>");
        }

        html.Append("</ul></body></html>");
        using var site = await TinySite.StartAsync(html.ToString());
        var model = new ScriptedModel(request =>
        {
            var last = request.Messages[^1].Content ?? "";
            if (last.StartsWith("tapped", StringComparison.Ordinal))
            {
                return ModelResponses.Done("passed", "opened");
            }

            if (last.Contains("button \"Item 3150\"", StringComparison.Ordinal))
            {
                return ModelResponses.Tap("button", "Item 3150");
            }

            return ModelResponses.Call("scroll_to", new { text = "Item 3150" });
        });

        var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = model,
            BaseUrl = site.Url,
            TestTitle = "web > scroll",
            AssertionTimeout = TimeSpan.FromSeconds(5),
            ActionTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(30),
            TestTimeout = TimeSpan.FromSeconds(60),
        });

        await using (session)
        {
            await session.App.OpenAsync("/");
            await Expect.That(session.Screen.GetByRole("button", "Item 3150")).ToHaveCountAsync(0);
            await session.Agent.ActAsync("open item 3150");
            await Expect.That(session.Screen.GetByRole("status")).ToContainTextAsync("Opened Item 3150");
            Assert.Contains("More of the page is off screen", model.Requests[0].Messages[0].Content, StringComparison.Ordinal);
        }
    }

    // Rows 1 to 5 show at first. Each scroll down on the page or the list loads five more, up to 30.
    private static DocumentWorld Rows()
    {
        return new DocumentWorld().Map("/rows", page =>
        {
            var status = page.Status("none");
            var list = page.TestId("rows", "list", "Rows");
            var shown = 0;
            void More()
            {
                for (var index = 0; index < 5 && shown < 30; index++)
                {
                    shown++;
                    var label = "Row " + shown.ToString(CultureInfo.InvariantCulture);
                    list.Children.Add(new DocumentElement
                    {
                        Role = "button",
                        Name = label,
                        OnTap = () => status.Name = status.Text = "Opened " + label,
                    });
                }
            }

            More();
            page.OnScroll = direction =>
            {
                if (direction == ScrollDirection.Down)
                {
                    More();
                }
            };
            list.OnScroll = page.OnScroll;
        });
    }

    private static ScriptedModel Sequence(params ModelResponse[] responses)
    {
        var next = 0;
        return new ScriptedModel(_ => next < responses.Length
            ? responses[next++]
            : ModelResponses.Done("failed", "The script ran out of responses."));
    }

    private static List<string> ToolResults(ScriptedModel model)
    {
        return model.Requests[^1].Messages
            .Where(message => message.Role == "tool")
            .Select(message => message.Content ?? "")
            .ToList();
    }

    private static CacheEntry ReadEntry(string directory)
    {
        var file = Assert.Single(Directory.GetFiles(directory, "*.json"));
        return System.Text.Json.JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(file), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
    }

    private static string TempCache()
    {
        return Path.Combine(Path.GetTempPath(), "e2e-tests", Guid.NewGuid().ToString("n"));
    }

    private readonly record struct Attempt(Exception? Error, int ModelCalls, int Replayed);

    private static async Task<Attempt> RunAsync(
        Func<TestContext, Task> body,
        DocumentWorld world,
        IAgentModel model,
        string? cacheDirectory = null)
    {
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(world),
            Model = model,
            BaseUrl = "https://rows.test",
            Cache = cacheDirectory is null ? null : new FileStepCache(cacheDirectory),
            CacheEnabled = cacheDirectory is not null,
            TestTitle = "rows > case",
            AssertionTimeout = TimeSpan.FromSeconds(2),
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            TestTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(5),
        });
        Exception? error = null;
        try
        {
            await body(session.Context);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);
        return new Attempt(error, session.ModelCalls, session.Replayed);
    }
}
