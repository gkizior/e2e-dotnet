// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using E2E.Engine;

namespace E2E.Tests.AgentAct;

[Collection(BrowserCollection.Name)]
public sealed class AgentActEngineOperationsAreBoundedByActionTimeoutTests
{
    [Fact]
    public async Task A_navigation_that_never_settles_costs_one_action_timeout_not_the_step_budget()
    {
        using var site = await TinySite.StartAsync(context => context.Request.Url!.AbsolutePath == "/hang"
            ? Task.Delay(Timeout.Infinite)
            : TinySite.RespondAsync(context, "<!doctype html><title>Home</title><h1>Home</h1>"));
        var model = new ScriptedModel(request => request.Messages.LastOrDefault(message => message.Role == "tool") is { } result
            ? ModelResponses.Done("failed", "navigation gave up: " + result.Content)
            : ModelResponses.Call("navigate", new { url = "/hang" }));
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new WebEngine(headless: true),
            Model = model,
            BaseUrl = site.Url,
            TestTitle = "act > hang",
            ActionTimeout = TimeSpan.FromSeconds(1),
            StepTimeout = TimeSpan.FromSeconds(60),
            TestTimeout = TimeSpan.FromSeconds(120),
        });

        Exception? error = null;
        var watch = Stopwatch.StartNew();
        try
        {
            await session.App.OpenAsync("/");
            watch.Restart();
            await session.Agent.ActAsync("open the hanging page", new ActOptions { Timeout = TimeSpan.FromSeconds(60) });
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);

        // The 60 s act budget is untouched: the hung navigation fails within its own operation bound and the
        // model concludes, well under the clock.
        var failure = Assert.IsType<AgentException>(error);
        Assert.NotEqual("STEP_TIMEOUT", failure.Code);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(15), "settled after " + watch.Elapsed);
        Assert.Contains("navigation gave up", failure.Message, StringComparison.Ordinal);
    }
}
