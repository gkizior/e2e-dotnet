// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.AgentReading;

public sealed class ReadingShapeTests
{
    /// <summary>
    /// Partial: the port compares the rendered screen and the scroll position between pages, and has no reading shape to
    /// compare directly. A row that keeps its name and its place but changes its text keeps the paging going. The same
    /// row, unchanged, stops it.
    /// </summary>
    [Fact]
    public async Task Keeps_paging_while_a_rows_text_moves_on_and_stops_when_it_stays_the_same()
    {
        var moving = await PageAsync(textMoves: 12);
        Assert.Contains("before the screen stopped moving", moving.Result, StringComparison.Ordinal);
        Assert.True(moving.Swipes >= 15);

        var still = await PageAsync(textMoves: 0);
        Assert.Contains("before the screen stopped moving", still.Result, StringComparison.Ordinal);
        Assert.True(still.Swipes <= 4);
    }

    private static async Task<(string Result, int Swipes)> PageAsync(int textMoves)
    {
        var swipes = 0;
        var world = new DocumentWorld().Map("/products", page =>
        {
            var row = new DocumentElement { Role = "listitem", Name = "Product", Text = "Product 1" };
            page.Roots.Add(row);
            page.OnScroll = _ =>
            {
                swipes++;
                if (swipes <= textMoves)
                {
                    row.Text = "Product " + (swipes + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                }
            };
        });
        string? result = null;
        var calls = 0;
        var model = new ScriptedModel(request =>
        {
            if (calls++ == 0)
            {
                return ModelResponses.Call("scroll_to", new { text = "Golden" });
            }

            result = request.Messages.Last(message => message.Role == "tool").Content;
            return ModelResponses.Done("passed", "gave up");
        });
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions { Engine = new ScrollLogEngine(world), Model = model, BaseUrl = "https://shop.test", TestTitle = "reading shape" });
        await session.App.OpenAsync("/products");
        await session.Agent.ActAsync("reach the golden product");
        session.Complete();
        return (result ?? "", swipes);
    }
}
