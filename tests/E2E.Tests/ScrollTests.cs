// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using E2E.Engine;

namespace E2E.Tests;

public sealed class ScrollTests
{
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

    // Rows 1 to 5 show at first. Each scroll down on the page or the list loads five more, up to 30.
    internal static DocumentWorld Rows()
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

    internal static ScriptedModel Sequence(params ModelResponse[] responses)
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

    internal static CacheEntry ReadEntry(string directory)
    {
        var file = Assert.Single(Directory.GetFiles(directory, "*.json"));
        return System.Text.Json.JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(file), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
    }

    internal static string TempCache()
    {
        return Path.Combine(Path.GetTempPath(), "e2e-tests", Guid.NewGuid().ToString("n"));
    }

    internal readonly record struct Attempt(Exception? Error, int ModelCalls, int Replayed);

    internal static async Task<Attempt> RunAsync(
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
