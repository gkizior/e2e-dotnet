// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using E2E.Engine;
using Microsoft.Playwright;

namespace E2E.Tests.Actions;

/// <summary>Every Playwright failure of a locator action lands on the right contract code.</summary>
public sealed class ClassifyActionErrorTests
{
    private static readonly string[] PreDispatchLog =
    [
        "waiting for getByRole('button')",
        "locator resolved to <button>Go</button>",
        "attempting click action",
        "waiting for element to be visible, enabled and stable",
        "element is not stable",
        "retrying click action",
    ];

    private static readonly string[] PostDispatchLog =
    [
        .. PreDispatchLog[..4],
        "element is visible, enabled and stable",
        "scrolling into view if needed",
        "done scrolling",
        "performing click action",
        "click action done",
        "waiting for scheduled navigations to finish",
    ];

    private static readonly LocatorAction Tap = new LocatorAction.Tap();

    [Fact(Skip = "https://github.com/hardkoded/e2e-dotnet/issues/107: ClassifyAction reclassifies an already classified error")]
    public void Passes_classified_errors_through_untouched_whatever_their_class()
    {
        var engine = new EngineException(EngineErrorCodes.NodeStale, "gone", retryable: true);
        Assert.Same(engine, WebErrors.ClassifyAction(engine, Tap));
        var runner = new TestException("INVALID_ARGUMENT", "bad");
        Assert.Same(runner, WebErrors.ClassifyAction(runner, Tap));
    }

    [Theory]
    [InlineData("strict mode violation: 2 elements", "NODE_STALE", true)]
    [InlineData("strict mode violation: 2 elements\nCall log:\n  - move and down action done", "ACTION_MAY_HAVE_COMMITTED", false)]
    [InlineData("strict mode violation: getByText('performing click action') resolved to 2 elements:\n    1) <p>click action done</p>\nCall log:\n  - waiting for getByText('performing click action')", "NODE_STALE", true)]
    [InlineData("element is detached from the DOM", "NODE_STALE", true)]
    [InlineData("Element is not an <input>, <textarea> or [contenteditable] element", "NOT_ACTIONABLE", false)]
    [InlineData("Target page, context or browser has been closed", "ENGINE_FAILURE", false)]
    public void Maps_text_to_code(string text, string code, bool retryable)
    {
        var error = WebErrors.ClassifyAction(new PlaywrightException(text), Tap);

        Assert.Equal(code, error.Code);
        Assert.Equal(retryable, error.Retryable);
    }

    [Fact]
    public void Maps_a_timeout_whose_log_ends_before_the_dispatch_to_NOT_ACTIONABLE()
    {
        var error = WebErrors.ClassifyAction(PwTimeout(PreDispatchLog), Tap);

        Assert.Equal("NOT_ACTIONABLE", error.Code);
        Assert.False(error.Retryable);
        Assert.Contains("tap did not become actionable", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cuts_a_not_actionable_timeout_to_its_headline_and_the_last_blocker_the_log_names()
    {
        string[] covered =
        [
            .. PreDispatchLog[..4],
            "element is visible, enabled and stable",
            "<div class=\"toast\">…</div> intercepts pointer events",
            "retrying click action",
            "waiting 100ms",
            "<div data-popover=\"true\">…</div> intercepts pointer events",
            "retrying click action",
        ];
        Assert.Equal(
            "tap did not become actionable in time: locator.click: Timeout 5000ms exceeded. <div data-popover=\"true\">…</div> intercepts pointer events",
            WebErrors.ClassifyAction(PwTimeout(covered), Tap).Message);
        Assert.Equal(
            "tap did not become actionable in time: locator.click: Timeout 5000ms exceeded. element is not stable",
            WebErrors.ClassifyAction(PwTimeout(PreDispatchLog), Tap).Message);
        var compressed = new System.TimeoutException(
            "locator.click: Timeout 5000ms exceeded.\nCall log:\n  - attempting click action\n    2 × waiting for element to be visible, enabled and stable\n      - element is not enabled\n    - retrying click action\n");
        Assert.Equal(
            "tap did not become actionable in time: locator.click: Timeout 5000ms exceeded. element is not enabled",
            WebErrors.ClassifyAction(compressed, Tap).Message);
    }

    [Fact]
    public void Keeps_a_not_actionable_timeout_whole_when_its_log_names_no_blocker()
    {
        foreach (var log in new[] { new[] { "waiting for getByRole('button')" }, new[] { "waiting for getByText('element is not visible')" } })
        {
            var waiting = PwTimeout(log);
            Assert.Equal("tap did not become actionable in time: " + waiting.Message, WebErrors.ClassifyAction(waiting, Tap).Message);
        }
    }

    [Fact]
    public void Maps_a_timeout_whose_log_reached_the_dispatch_to_ACTION_MAY_HAVE_COMMITTED()
    {
        var error = WebErrors.ClassifyAction(PwTimeout(PostDispatchLog), Tap);

        Assert.Equal("ACTION_MAY_HAVE_COMMITTED", error.Code);
        Assert.False(error.Retryable);
        Assert.Contains("after its input was dispatched", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Treats_a_log_that_stopped_at_performing_as_uncertain_the_input_may_be_in_flight()
    {
        var inFlight = PostDispatchLog[..(Array.IndexOf(PostDispatchLog, "performing click action") + 1)];

        Assert.Equal("ACTION_MAY_HAVE_COMMITTED", WebErrors.ClassifyAction(PwTimeout(inFlight), Tap).Code);
    }

    [Fact]
    public void Treats_an_action_the_operation_deadline_cut_off_as_uncertain_Playwright_never_said_whether_the_input_landed()
    {
        var cut = new EngineException(EngineErrorCodes.OperationTimeout, "tap timed out", retryable: false);

        var error = WebErrors.ClassifyAction(cut, Tap);

        Assert.Equal("ACTION_MAY_HAVE_COMMITTED", error.Code);
        Assert.False(error.Retryable);
        Assert.Same(cut, error.InnerException);
    }

    [Fact]
    public void Keeps_a_bare_timeout_without_a_call_log_a_plain_actionability_miss()
    {
        var error = new System.TimeoutException("Timeout 5000ms exceeded");

        Assert.Equal("NOT_ACTIONABLE", WebErrors.ClassifyAction(error, new LocatorAction.Press("Enter")).Code);
    }

    [Fact]
    public void Never_echoes_a_sensitive_fill_value_in_the_message_or_through_the_cause()
    {
        const string secret = "hunter2-plaintext";
        var raw = new PlaywrightException($"locator.fill(\"{secret}\"): Element is not an <input>, <textarea> or [contenteditable] element");

        var error = WebErrors.ClassifyAction(raw, new LocatorAction.Fill(secret, Sensitive: true));

        Assert.Equal("NOT_ACTIONABLE", error.Code);
        Assert.DoesNotContain(secret, error.Message, StringComparison.Ordinal);
        Assert.Contains("[redacted]", error.Message, StringComparison.Ordinal);
        Assert.Null(error.InnerException);
    }

    [Fact]
    public void Keeps_the_raw_cause_for_an_ordinary_fill()
    {
        var raw = new PlaywrightException("Element is not an <input>");

        var error = WebErrors.ClassifyAction(raw, new LocatorAction.Fill("gamma", Sensitive: false));

        Assert.Same(raw, error.InnerException);
    }

    // Playwright for .NET reports a timeout as a TimeoutException.
    private static System.TimeoutException PwTimeout(IEnumerable<string> callLog)
    {
        return new System.TimeoutException("locator.click: Timeout 5000ms exceeded.\nCall log:\n" + string.Join('\n', callLog.Select(line => "  - " + line)) + "\n");
    }
}
