// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;
using Microsoft.Playwright;

namespace E2E.Tests;

[Collection(BrowserCollection.Name)]
public sealed class EngineErrorTests
{
    [Fact]
    public void Only_stale_reads_can_claim_to_be_retryable()
    {
        var stale = new EngineException(EngineErrorCodes.NodeStale, "gone", retryable: true);
        Assert.Equal("NODE_STALE", stale.Code);
        Assert.True(stale.Retryable);

        var coerced = new EngineException(EngineErrorCodes.NotActionable, "blocked", retryable: true);
        Assert.Equal("ENGINE_FAILURE", coerced.Code);
        Assert.False(coerced.Retryable);

        Assert.False(new EngineException(EngineErrorCodes.NodeStale, "gone").Retryable);
    }

    [Fact]
    public void An_action_timeout_before_dispatch_is_not_actionable()
    {
        var cause = new System.TimeoutException(
            "Timeout 5000ms exceeded.\nCall log:\n  - waiting for element to be visible, enabled and stable\n  - element is not enabled");

        var error = WebErrors.ClassifyAction(cause, new LocatorAction.Tap());

        Assert.Equal("NOT_ACTIONABLE", error.Code);
        Assert.False(error.Retryable);
        Assert.Contains("tap did not become actionable in time", error.Message, StringComparison.Ordinal);
        Assert.Contains("element is not enabled", error.Message, StringComparison.Ordinal);
        Assert.Same(cause, error.InnerException);
    }

    [Fact]
    public void An_action_timeout_after_dispatch_may_have_committed()
    {
        var cause = new System.TimeoutException(
            "Timeout 5000ms exceeded.\nCall log:\n  - performing click action\n  - click action done\n  - waiting for scheduled navigations to finish");

        var error = WebErrors.ClassifyAction(cause, new LocatorAction.Tap());

        Assert.Equal("ACTION_MAY_HAVE_COMMITTED", error.Code);
        Assert.False(error.Retryable);
    }

    [Fact]
    public void A_detached_element_is_a_retryable_stale_node()
    {
        var error = WebErrors.ClassifyAction(new PlaywrightException("Element is not attached to the DOM"), new LocatorAction.Check());

        Assert.Equal("NODE_STALE", error.Code);
        Assert.True(error.Retryable);
    }

    [Fact]
    public void A_field_that_does_not_take_text_is_not_actionable()
    {
        var error = WebErrors.ClassifyAction(new PlaywrightException("Error: Element is not an <input>, <textarea> or [contenteditable] element"), new LocatorAction.Fill("x"));

        Assert.Equal("NOT_ACTIONABLE", error.Code);
    }

    [Fact]
    public void A_sensitive_fill_redacts_its_value_and_drops_the_cause()
    {
        var error = WebErrors.ClassifyAction(new PlaywrightException("fill(\"hunter2\") failed"), new LocatorAction.Fill("hunter2", Sensitive: true));

        Assert.Equal("ENGINE_FAILURE", error.Code);
        Assert.DoesNotContain("hunter2", error.Message, StringComparison.Ordinal);
        Assert.Contains("[redacted]", error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Navigation_and_reads_map_to_engine_codes()
    {
        Assert.Equal("OPERATION_TIMEOUT", WebErrors.Translate(new System.TimeoutException("Timeout 5000ms exceeded."), "navigate to /").Code);
        Assert.Equal("ENGINE_FAILURE", WebErrors.Translate(new PlaywrightException("net::ERR_CONNECTION_REFUSED"), "navigate to /").Code);

        var stale = WebErrors.NavigationStaleOr(new PlaywrightException("Execution context was destroyed, most likely because of a navigation"), "observe");
        Assert.Equal("NODE_STALE", stale.Code);
        Assert.True(stale.Retryable);
        Assert.Equal("ENGINE_FAILURE", WebErrors.NavigationStaleOr(new PlaywrightException("boom"), "observe").Code);
    }

    [Fact]
    public async Task Chromium_reports_engine_codes_instead_of_playwright_errors()
    {
        using var site = await TinySite.StartAsync("""
            <!DOCTYPE html>
            <html><body><button type="button" disabled>Save</button></body></html>
            """);
        var session = await new WebEngine(headless: true).StartAsync(new EngineStartOptions { ActionTimeout = TimeSpan.FromMilliseconds(500) }, CancellationToken.None);

        await using (session)
        {
            await session.OpenAsync(site.Url, CancellationToken.None);
            var observation = await session.ObserveAsync(CancellationToken.None);
            var button = Flatten(observation.Roots).First(node => node.Role == "button");

            var blocked = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(button, new LocatorAction.Tap(), CancellationToken.None));
            Assert.Equal("NOT_ACTIONABLE", blocked.Code);

            var missing = new SemanticNode { Ref = "e9999", Role = "button" };
            var stale = await Assert.ThrowsAsync<EngineException>(() => session.PerformAsync(missing, new LocatorAction.Tap(), CancellationToken.None));
            Assert.Equal("NODE_STALE", stale.Code);

            var unreachable = await Assert.ThrowsAsync<EngineException>(() => session.OpenAsync("http://127.0.0.1:1/", CancellationToken.None));
            Assert.Equal("ENGINE_FAILURE", unreachable.Code);
        }
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
}
