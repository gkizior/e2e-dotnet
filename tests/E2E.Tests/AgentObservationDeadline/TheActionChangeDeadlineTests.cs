// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.AgentObservationDeadline;

/// <summary>The action's change window includes device capture time and delays before observation.</summary>
public sealed class TheActionChangeDeadlineTests
{
    // Upstream also runs this with the delay in a custom executor before it observes. The port has no custom executor:
    // SettleObservationTests.Keeps_a_fresh_stability_check_when_the_action_window_expired_before_observation covers that window.
    [Fact]
    public async Task Counts_time_spent_in_the_capture_before_checking_stability()
    {
        var engine = new SlowCaptureEngine(new DocumentWorld().Map("/", page => page.Paragraph("End of list")), TimeSpan.FromMilliseconds(700));
        var model = new ScriptedModel(request =>
            request.Messages.Any(message => message.Role == "tool")
                ? ModelResponses.Done("passed", "the list has not changed")
                : ModelResponses.Call("scroll", new { direction = "down" }));
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = engine,
            Model = model,
            BaseUrl = "https://app.test",
            CacheEnabled = false,
            TestTitle = "observes after an unchanged scroll",
        });
        await session.App.OpenAsync("/");
        var result = await session.Agent.ActAsync("scroll once and inspect the result");
        session.Complete();

        Assert.Equal("the list has not changed", result.Summary);
        Assert.Equal(2, engine.Captures);
    }
}
