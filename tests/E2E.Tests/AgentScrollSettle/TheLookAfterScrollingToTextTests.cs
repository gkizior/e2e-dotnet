// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Tests.AgentScrollSettle;

/// <summary>
/// The look after scrolling to text. Upstream's "keeps the stability check without another change wait after N pages"
/// is not ported: it covers an engine that cannot scroll a node into view, and every port engine can. Upstream's fake
/// engine shows nothing of a scroll until the late change, but a browser shows the scroll at once, so the testbed page
/// keeps changing until the late change lands. The test therefore checks only part of upstream's "still waits for the
/// final scrollIntoView operation to change the screen": a single late change after a scroll cannot be shown, because
/// the settle shape includes the scroll position, so it carries a name of its own.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class TheLookAfterScrollingToTextTests
{
    private const string Page = """
        <!doctype html>
        <html>
        <body>
          <p role="status">waiting</p>
          <p id="progress">idle</p>
          <div style="height:3000px"></div>
          <button id="row">Final row</button>
          <script>
            const result = document.querySelector('[role="status"]');
            const progress = document.getElementById('progress');
            const row = document.getElementById('row');
            const observer = new IntersectionObserver((entries) => {
              if (!entries[0].isIntersecting) return;
              observer.disconnect();
              // The operation keeps changing the screen while it runs: a real scroll shows at once, so a screen that
              // only changed once would look held still before the late change lands.
              let step = 0;
              const ticker = setInterval(() => { progress.textContent = 'loading ' + (++step); }, 20);
              setTimeout(() => {
                clearInterval(ticker);
                progress.textContent = 'loaded';
                result.textContent = 'in view';
              }, 250);
            });
            observer.observe(row);
            row.addEventListener('click', () => { result.textContent = 'tapped'; });
          </script>
        </body>
        </html>
        """;

    [Fact]
    public async Task Waits_for_a_screen_the_final_scroll_into_view_operation_keeps_changing_to_hold_still()
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
