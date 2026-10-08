// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Tests.AgentScrollSettle;

/// <summary>
/// The look after scrolling to text. Upstream's "keeps the stability check without another change wait after N pages"
/// is not ported: it covers an engine that cannot scroll a node into view, and every port engine can.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class TheLookAfterScrollingToTextTests
{
    private const string Page = """
        <!doctype html>
        <html>
        <body>
          <p role="status">waiting</p>
          <div style="height:3000px"></div>
          <button id="row">Final row</button>
          <script>
            const result = document.querySelector('[role="status"]');
            const row = document.getElementById('row');
            new IntersectionObserver((entries) => {
              if (entries[0].isIntersecting) setTimeout(() => { result.textContent = 'in view'; }, 250);
            }).observe(row);
            row.addEventListener('click', () => { result.textContent = 'tapped'; });
          </script>
        </body>
        </html>
        """;

    [Fact]
    public async Task Still_waits_for_the_final_scroll_into_view_operation_to_change_the_screen()
    {
        using var site = await TinySite.StartAsync(context => TinySite.RespondAsync(context, Page));
        string? afterScroll = null;
        var calls = 0;
        var model = new ScriptedModel(request =>
        {
            var prompt = string.Join('\n', request.Messages.Select(message => message.Content));
            switch (calls++)
            {
                case 0:
                    return ModelResponses.Call("scroll_to", new { text = "Final row" });
                case 1:
                    afterScroll = request.Messages.Last(message => message.Role == "tool").Content;
                    var reference = Regex.Match(afterScroll!, @"button ""Final row"" \[ref=(e\d+)\]").Groups[1].Value;
                    return ModelResponses.Call("tap", new { @ref = reference });
                default:
                    return ModelResponses.Done("passed", "tapped the final row");
            }
        });
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = model,
            BaseUrl = site.Url,
            CacheEnabled = false,
            TestTitle = "reaches and taps the row",
            AssertionTimeout = TimeSpan.FromSeconds(5),
            ActionTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(20),
            TestTimeout = TimeSpan.FromSeconds(30),
        });
        Exception? error = null;
        try
        {
            await session.Context.App.OpenAsync("/");
            await session.Context.Agent.ActAsync("reach and tap the final row");
            await Expect.That(session.Context.Screen.GetByRole("status")).ToHaveTextAsync("tapped");
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);
        Assert.Null(error);
        Assert.Contains("in view", afterScroll, StringComparison.Ordinal);
    }
}
