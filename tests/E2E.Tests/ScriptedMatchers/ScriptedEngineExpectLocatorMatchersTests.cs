// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text.RegularExpressions;
using E2E.Tests.ScriptedScreen;

namespace E2E.Tests.ScriptedMatchers;

/// <summary>
/// <c>expect(locator)</c> over the scripted reference screen: every matcher and a few negations pass, every
/// failure names the observed state, a matcher honors its timeout, and an ambiguous locator fails at once.
/// Upstream runs each case as a test in the real runner and reads the report. The port has no runner and no
/// report, so each case runs in the test body and reads the thrown error. Not ported: the report checks
/// (valid report, one result per test, step api names and statuses) and the error <c>category</c> and
/// <c>details</c>. The message names the locator and the expected value instead.
/// </summary>
public sealed class ScriptedEngineExpectLocatorMatchersTests
{
    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(50);

    [Fact]
    public async Task Passes_every_matcher_and_its_negation_on_the_reference_screen()
    {
        using var scene = ReferenceScene.Create();
        var screen = scene.Screen;
        await Expect.That(screen.GetByRole("heading", new RoleOptions { Name = "Dashboard" })).ToBeVisibleAsync();
        await Expect.That(screen.GetByText("Hidden content")).ToBeHiddenAsync();
        await Expect.That(screen.GetByText("Hidden content")).ToBeAttachedAsync();
        await Expect.That(screen.GetByText("Never rendered")).ToBeHiddenAsync();
        await Expect.That(screen.GetByRole("button", "Submit")).ToBeEnabledAsync();
        await Expect.That(screen.GetByRole("button", "Disabled action")).ToBeDisabledAsync();
        await Expect.That(screen.GetByRole("switch", "Dark mode")).ToBeCheckedAsync();
        await Expect.That(screen.GetByRole("tab", "All")).ToBeSelectedAsync();
        await screen.GetByRole("button", "Menu").TapAsync();
        await Expect.That(screen.GetByRole("button", "Menu")).ToBeExpandedAsync();
        await screen.GetByLabel("Focus target").FocusAsync();
        await Expect.That(screen.GetByLabel("Focus target")).ToBeFocusedAsync();
        await Expect.That(screen.GetByRole("heading", new RoleOptions { Level = 1 })).ToHaveTextAsync("Dashboard");
        await Expect.That(screen.GetByRole("heading", new RoleOptions { Level = 1 })).ToHaveTextAsync(new Regex("^Dash"));
        await Expect.That(screen.GetByTestId("card")).ToContainTextAsync("body");
        await Expect.That(screen.GetByLabel("Search")).ToHaveValueAsync("hello-value");
        await Expect.That(screen.GetByLabel("Email")).ToHaveValueAsync("");
        await Expect.That(screen.GetByLabel("Notes")).ToHaveValueAsync("line1\n\nline2  ");
        await Expect.That(screen.GetByLabel("Notes")).ToHaveValueAsync(new Regex("^line1\n\nline2 {2}$"));
        await Expect.That(screen.GetByTestId("card")).ToHaveAttributeAsync("data-state");
        await Expect.That(screen.GetByTestId("card")).ToHaveAttributeAsync("data-state", "open");
        await Expect.That(screen.GetByTestId("card")).ToHaveAttributeAsync("class", new Regex("active"));
        await Expect.That(screen.GetByRole("list", "Todos")).ToHaveAccessibleNameAsync("Todos");
        await Expect.That(screen.GetByTestId("todo")).ToHaveCountAsync(3);
        await Expect.That(screen.GetByTestId("todo")).ToHaveTextAsync(["Write spec", new Regex("^Ship"), "Release"]);
        await Expect.That(screen.GetByTestId("todo")).ToContainTextAsync(["spec", "runner", "Release"]);
        await Expect.That(screen.GetByTestId("todo")).ToContainTextAsync(["spec", "Release"]);
        await Expect.That(screen.GetByTestId("missing")).ToHaveTextAsync([]);
        await Expect.That(screen.GetByTestId("missing")).ToHaveCountAsync(0);

        // Half a second: a negation holds for the smaller of the grace window and the budget.
        var soon = TimeSpan.FromMilliseconds(500);
        await Expect.That(screen.GetByText("Hidden content")).Not.ToBeVisibleAsync(timeout: soon);
        await Expect.That(screen.GetByLabel("Notes")).Not.ToHaveValueAsync("line1 line2", timeout: soon);
        await Expect.That(screen.GetByTestId("todo")).Not.ToHaveCountAsync(2, timeout: soon);
        await Expect.That(screen.GetByTestId("todo")).Not.ToHaveTextAsync(["Write spec", "Ship runner"], timeout: soon);
    }

    [Fact]
    public async Task Names_the_expected_and_observed_state_in_every_failure_message()
    {
        using var scene = ReferenceScene.Create();
        var screen = scene.Screen;
        var failures = new (string Name, Func<Task> Run, string[] Fragments)[]
        {
            ("toBeVisible fails on a hidden node", () => Expect.That(screen.GetByText("Hidden content")).ToBeVisibleAsync(timeout: Fast),
                ["getByText(\"Hidden content\")", ".toBeVisible failed", "expected visible", "observed hidden (match count 1)"]),
            ("toBeHidden fails on a visible node", () => Expect.That(screen.GetByRole("button", "Submit")).ToBeHiddenAsync(timeout: Fast),
                ["expected hidden or absent", "observed visible (match count 1)"]),
            ("toBeAttached fails on a missing node", () => Expect.That(screen.GetByText("Never rendered")).ToBeAttachedAsync(timeout: Fast),
                ["expected attached", "observed no node (match count 0)"]),
            ("toBeEnabled fails on a disabled node", () => Expect.That(screen.GetByRole("button", "Disabled action")).ToBeEnabledAsync(timeout: Fast),
                ["expected enabled", "observed states: disabled"]),
            ("toBeChecked fails on an unchecked node", () => Expect.That(screen.GetByRole("checkbox", "Notifications")).ToBeCheckedAsync(timeout: Fast),
                ["expected checked", "observed "]),
            ("toBeExpanded fails on a collapsed node", () => Expect.That(screen.GetByRole("button", "Menu")).ToBeExpandedAsync(timeout: Fast),
                ["expected expanded", "observed "]),
            ("toHaveText fails on other text", () => Expect.That(screen.GetByRole("heading", new RoleOptions { Level = 1 })).ToHaveTextAsync("Recent", timeout: Fast),
                ["expected text \"Recent\"", "observed text \"Dashboard\""]),
            ("toContainText fails on absent text", () => Expect.That(screen.GetByTestId("card")).ToContainTextAsync("footer", timeout: Fast),
                ["expected text containing \"footer\"", "observed text \"Card body\""]),
            ("toHaveValue fails on a node without a value", () => Expect.That(screen.GetByRole("heading", new RoleOptions { Level = 1 })).ToHaveValueAsync("Dashboard", timeout: Fast),
                ["expected value \"Dashboard\"", "observed no value (not a form control)"]),
            ("toHaveValue never normalizes the value", () => Expect.That(screen.GetByLabel("Notes")).ToHaveValueAsync("line1 line2", timeout: Fast),
                ["observed value \"line1\\n\\nline2  \""]),
            ("toHaveAttribute fails on an absent attribute", () => Expect.That(screen.GetByTestId("card")).ToHaveAttributeAsync("hidden", timeout: Fast),
                ["expected attribute \"hidden\"", "observed attribute \"hidden\" absent"]),
            ("toHaveAttribute fails on another value", () => Expect.That(screen.GetByTestId("card")).ToHaveAttributeAsync("data-state", "closed", timeout: Fast),
                ["expected attribute \"data-state\" \"closed\"", "observed attribute \"data-state\" \"open\""]),
            ("toHaveAccessibleName fails on another name", () => Expect.That(screen.GetByRole("list", "Todos")).ToHaveAccessibleNameAsync("Done", timeout: Fast),
                ["expected accessible name \"Done\"", "observed accessible name \"Todos\""]),
            ("toHaveCount fails on another count", () => Expect.That(screen.GetByTestId("todo")).ToHaveCountAsync(2, timeout: Fast),
                ["expected count 2", "observed count 3 (match count 3)"]),
            ("the list form of toHaveText fails on a shorter list", () => Expect.That(screen.GetByTestId("todo")).ToHaveTextAsync(["Write spec", "Ship runner"], timeout: Fast),
                ["expected text [\"Write spec\", \"Ship runner\"]", "observed text [\"Write spec\", \"Ship runner\", \"Release\"] (match count 3)"]),
            ("the negated list form of toContainText fails when one match contains the entry", () => Expect.That(screen.GetByTestId("todo")).Not.ToContainTextAsync(["runner"], timeout: Fast),
                [".not.toContainText failed", "expected not text containing [\"runner\"]"]),
            ("a negated matcher fails while the condition holds", () => Expect.That(screen.GetByRole("button", "Submit")).Not.ToBeVisibleAsync(timeout: Fast),
                [".not.toBeVisible failed", "expected not visible", "observed visible (match count 1)"]),
        };

        foreach (var (name, run, fragments) in failures)
        {
            var error = await Assert.ThrowsAsync<TestException>(run);
            Assert.True(error.Code == "ASSERTION_FAILED", name + ": " + error.Code + " " + error.Message);
            foreach (var fragment in fragments)
            {
                Assert.True(error.Message.Contains(fragment, StringComparison.Ordinal), name + ": " + error.Message);
            }
        }
    }

    [Fact]
    public async Task Refuses_to_judge_a_secure_field_with_POLICY_DENIED_negated_and_in_list_form_without_echoing_the_value()
    {
        using var scene = ReferenceScene.Create();
        var screen = scene.Screen;
        var password = screen.GetByLabel("Password");
        var denials = new (string Name, Func<Task> Run)[]
        {
            ("toHaveValue on a secure field is denied", () => Expect.That(password).ToHaveValueAsync("")),
            ("a negated toHaveValue on a secure field is denied", () => Expect.That(password).Not.ToHaveValueAsync("")),
            ("toHaveText on a secure field is denied", () => Expect.That(password).ToHaveTextAsync("")),
            ("a negated toHaveText on a secure field is denied", () => Expect.That(password).Not.ToHaveTextAsync("")),
            ("the list form of toHaveText over a secure field is denied", () => Expect.That(screen.GetByRole("textbox")).ToHaveTextAsync(["", "", "", ""])),
        };

        foreach (var (name, run) in denials)
        {
            var error = await Assert.ThrowsAsync<TestException>(run);
            Assert.True(error.Code == "POLICY_DENIED", name + ": " + error.Code + " " + error.Message);
            Assert.Contains("reading values from a secure field is denied", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("hunter2", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Honors_an_explicit_timeout_and_fails_an_ambiguous_locator_at_its_deadline()
    {
        using var scene = ReferenceScene.Create();
        var screen = scene.Screen;
        var watch = Stopwatch.StartNew();
        var timed = await Assert.ThrowsAsync<TestException>(() => Expect.That(screen.GetByText("Never rendered")).ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(250)));
        Assert.Equal("ASSERTION_FAILED", timed.Code);
        Assert.Contains("(match count 0)", timed.Message, StringComparison.Ordinal);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(250), watch.Elapsed.ToString());

        // Upstream fails an ambiguous locator at once. The port waits out the matcher's timeout, then fails (COMPATIBILITY.md).
        watch.Restart();
        var ambiguous = await Assert.ThrowsAsync<TestException>(() => Expect.That(screen.GetByText("Duplicated")).ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(250)));
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(250), watch.Elapsed.ToString());
        Assert.Equal("STRICT_MODE", ambiguous.Code);
        Assert.Contains("getByText(\"Duplicated\")", ambiguous.Message, StringComparison.Ordinal);
        Assert.Contains("(match count 2)", ambiguous.Message, StringComparison.Ordinal);
    }
}
