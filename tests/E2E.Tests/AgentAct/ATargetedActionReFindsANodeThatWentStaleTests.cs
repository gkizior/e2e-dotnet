// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Tests.AgentAct;

/// <summary>
/// A targeted action re-finds a node that went stale. Upstream also checks that the step recorded a relocation
/// capture. The port has no step events, so the page forces a stale node on every tap instead.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class ATargetedActionReFindsANodeThatWentStaleTests
{
    /// <summary>
    /// Upstream's /churn page: a list that remounts and rotates its rows the first time the pointer enters them after
    /// every render. Every tap on a ref from the last look lands on a detached node once, while "Tap me" stays on
    /// screen under a new element. Each successful tap re-arms it.
    /// </summary>
    private const string ChurnPage = """
        <!doctype html>
        <html>
        <head><title>Churn</title></head>
        <body>
          <h1>Churn</h1>
          <output id="progress" role="status" aria-label="Progress">0 / 3</output>
          <ul id="rows"></ul>
          <script>
            let armed = true;
            let rotation = 0;
            let taps = 0;
            const labels = ['Decoy one', 'Tap me', 'Decoy two'];
            function render() {
              const rows = document.getElementById('rows');
              rows.replaceChildren();
              for (let i = 0; i < labels.length; i += 1) {
                const label = labels[(i + rotation) % labels.length];
                const li = document.createElement('li');
                const button = document.createElement('button');
                button.textContent = label;
                if (label === 'Tap me') {
                  button.addEventListener('click', () => {
                    taps += 1;
                    armed = true;
                    document.getElementById('progress').textContent = taps + ' / 3';
                  });
                }
                li.append(button);
                rows.append(li);
              }
            }
            document.getElementById('rows').addEventListener('mouseover', () => {
              if (!armed) return;
              armed = false;
              rotation += 1;
              render();
            });
            render();
          </script>
        </body>
        </html>
        """;

    [Fact]
    public async Task Taps_the_remounting_control_three_times_without_a_failed_tap()
    {
        using var site = await TinySite.StartAsync(context => TinySite.RespondAsync(context, ChurnPage));
        var turns = 0;
        // The ref comes from the newest screen that lists the button, the way a model reads its history.
        var model = new ScriptedModel(request => turns++ < 3
            ? ModelResponses.Call("tap", new { @ref = ButtonRef(request) })
            : ModelResponses.Done("passed", "tapped three times"));

        var result = await RunAsync(site, model);

        Assert.Equal(3, result.Actions);
        foreach (var request in model.Requests.Skip(1).Take(3))
        {
            Assert.DoesNotContain("failed", request.Messages.Last(message => message.Role == "tool").Content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Lets_batched_actions_keep_addressing_the_screen_the_turn_saw_after_the_rows_remounted_between_them()
    {
        using var site = await TinySite.StartAsync(context => TinySite.RespondAsync(context, ChurnPage));
        var turns = 0;
        var model = new ScriptedModel(request =>
        {
            if (turns++ > 0)
            {
                return ModelResponses.Done("passed", "tapped three times");
            }

            // Three taps in one turn, all by the opening screen's ref. The first tap remounts the rows, so by the
            // second the ref names an element that is gone, and the newest screen lists the button anew.
            var arguments = ModelResponses.Call("tap", new { @ref = ButtonRef(request) }).ToolCalls[0].Arguments;
            return new ModelResponse
            {
                ToolCalls = [.. Enumerable.Range(1, 3).Select(index => new ModelToolCall { Id = "call_tap_" + index, Name = "tap", Arguments = arguments })],
            };
        });

        var result = await RunAsync(site, model);

        Assert.Equal(3, result.Actions);
        Assert.Equal(2, result.ModelCalls);
        foreach (var message in model.Requests[1].Messages.Where(message => message.Role == "tool"))
        {
            Assert.DoesNotContain("failed", message.Content, StringComparison.Ordinal);
        }
    }

    private static string ButtonRef(ModelRequest request)
    {
        var text = string.Join('\n', request.Messages.Select(message => message.Content));
        return Regex.Matches(text, @"button ""Tap me"" \[ref=(e\d+)\]")[^1].Groups[1].Value;
    }

    private static async Task<ActResult> RunAsync(TinySite site, ScriptedModel model)
    {
        ActResult? result = null;
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = model,
            BaseUrl = site.Url,
            CacheEnabled = false,
            TestTitle = "default agent taps a control that remounts under it",
            AssertionTimeout = TimeSpan.FromSeconds(5),
            ActionTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(20),
            TestTimeout = TimeSpan.FromSeconds(30),
        });
        Exception? error = null;
        try
        {
            await session.Context.App.OpenAsync("/churn");
            result = await session.Context.Agent.ActAsync("tap the \"Tap me\" button three times");
            await Expect.That(session.Context.Screen.GetByRole("status")).ToHaveTextAsync("3 / 3");
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);
        Assert.Null(error);
        return result!;
    }
}
