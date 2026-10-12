// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.AgentReading;

/// <summary>
/// The port reads a plain, case-insensitive substring, so a reading has no word boundary and skips no sentence. So
/// these upstream its are not ported: "reads a whole label, or the text bounded by nothing of a word, never a longer
/// number sharing its prefix" and "skips a sentence that mentions the text and a hidden node".
/// </summary>
public sealed class NodeReadingTests
{
    /// <summary>Partial: the port reads through <c>scroll_to</c> and shows which node it brought into view, with no direct read of the node index.</summary>
    [Fact]
    public async Task Reads_inside_the_list_scroll_to_is_given_and_the_whole_screen_without_one()
    {
        static DocumentWorld World() => new DocumentWorld().Map("/ledger", page =>
        {
            page.Button("Jump to Row 333");
            var list = new DocumentElement { Role = "list", Name = "Ledger" };
            list.Children.Add(new DocumentElement { Role = "listitem", Text = "Row 1" });
            list.Children.Add(new DocumentElement { Role = "listitem", Text = "Row 2" });
            list.OnScroll = _ =>
            {
                if (list.Children.Count == 2)
                {
                    list.Children.Add(new DocumentElement { Role = "listitem", Text = "Row 333" });
                }
            };
            page.Roots.Add(list);
        });

        var inList = await ScrollToAsync(World(), new { text = "Row 333", role = "list", name = "Ledger" });
        Assert.Equal(["Row 333"], inList.ScrolledIntoView);
        Assert.True(inList.ListSwipes >= 1);

        var onScreen = await ScrollToAsync(World(), new { text = "Row 333" });
        Assert.Equal(["Jump to Row 333"], onScreen.ScrolledIntoView);
        Assert.Equal(0, onScreen.ListSwipes + onScreen.ViewportSwipes);
    }

    private static async Task<ScrollLogEngine> ScrollToAsync(DocumentWorld world, object arguments)
    {
        var engine = new ScrollLogEngine(world);
        var calls = 0;
        var model = new ScriptedModel(_ => calls++ == 0 ? ModelResponses.Call("scroll_to", arguments) : ModelResponses.Done("passed", "scrolled"));
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions { Engine = engine, Model = model, BaseUrl = "https://ledger.test", TestTitle = "reading" });
        await session.App.OpenAsync("/ledger");
        await session.Agent.ActAsync("reach Row 333");
        session.Complete();
        return engine;
    }
}
