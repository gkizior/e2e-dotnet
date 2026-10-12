// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Tests.ScriptedScreen;

namespace E2E.Tests.ScriptedReads;

/// <summary>
/// Locator reads over the scripted reference screen: every read answers from the current tree without
/// waiting, secure fields refuse value reads, and malformed refinements are refused before any query runs.
/// Upstream runs each case as a test in the real runner and reads the report. The port has no runner and no
/// report, so each case runs in the test body. Not ported: <c>textContent</c> of an image is <c>null</c> upstream, but the port answers the node's name; <c>inputValue</c> of a heading is the empty string upstream, but the port answers <c>null</c>; and the report checks (valid report, the four
/// <c>locator.waitFor</c> steps and the label of the first) and the error <c>category</c>. A <c>filter</c>
/// with no predicate cannot be written with the typed overloads, so the port refuses a null one.
/// </summary>
public sealed class ScriptedEngineLocatorReadsAndQueriesTests
{
    [Fact]
    public async Task Reads_every_field_count_and_list_without_waiting_except_the_text_of_an_image_and_the_value_of_a_heading()
    {
        using var scene = ReferenceScene.Create();
        var screen = scene.Screen;
        Assert.Equal("Dashboard", await screen.GetByRole("heading", new RoleOptions { Level = 1 }).TextContentAsync());
        Assert.Equal("hello-value", await screen.GetByLabel("Search").InputValueAsync());
        Assert.Equal("card active", await screen.GetByTestId("card").GetAttributeAsync("class"));
        Assert.Null(await screen.GetByTestId("card").GetAttributeAsync("missing"));
        Assert.True(await screen.GetByRole("button", "Submit").IsVisibleAsync());
        Assert.False(await screen.GetByText("Hidden content").IsVisibleAsync());
        Assert.True(await screen.GetByText("Hidden content").IsHiddenAsync());
        Assert.False(await screen.GetByText("Never rendered").IsVisibleAsync());
        Assert.True(await screen.GetByText("Never rendered").IsHiddenAsync());
        Assert.True(await screen.GetByRole("button", "Submit").IsEnabledAsync());
        Assert.False(await screen.GetByRole("button", "Disabled action").IsEnabledAsync());
        Assert.True(await screen.GetByRole("button", "Disabled action").IsDisabledAsync());
        Assert.True(await screen.GetByRole("switch", "Dark mode").IsCheckedAsync());
        Assert.False(await screen.GetByRole("checkbox", "Notifications").IsCheckedAsync());
        Assert.Equal(new BoundingBox(20, 200, 100, 40), await screen.GetByRole("button", "Submit").BoundingBoxAsync());
        Assert.Null(await screen.GetByRole("heading", new RoleOptions { Level = 1 }).BoundingBoxAsync());
        Assert.Equal(3, await screen.GetByTestId("todo").CountAsync());
        Assert.Equal(0, await screen.GetByTestId("missing").CountAsync());
        var items = await screen.GetByTestId("todo").AllAsync();
        Assert.Equal(3, items.Count);
        var texts = new List<string>();
        foreach (var item in items)
        {
            texts.Add(await item.TextContentAsync() ?? "");
        }

        Assert.Equal(["Write spec", "Ship runner", "Release"], texts);
        Assert.Equal(texts, await screen.GetByTestId("todo").AllTextContentsAsync());
        Assert.Empty(await screen.GetByTestId("missing").AllAsync());
        Assert.Empty(await screen.GetByTestId("missing").AllTextContentsAsync());
        await screen.GetByRole("button", "Submit").WaitForAsync();
        await screen.GetByRole("button", "Submit").WaitForAsync(new LocatorWaitForOptions { State = WaitForState.Visible });
        await screen.GetByText("Hidden content").WaitForAsync(new LocatorWaitForOptions { State = WaitForState.Hidden });
        await screen.GetByText("Never rendered").WaitForAsync(new LocatorWaitForOptions { State = WaitForState.Hidden });

        // A secure field still answers state reads.
        var password = screen.GetByLabel("Password");
        Assert.True(await password.IsEnabledAsync());
        Assert.False(await password.IsCheckedAsync());
        Assert.True(await password.IsVisibleAsync());
        Assert.Equal(new BoundingBox(10, 140, 200, 30), await password.BoundingBoxAsync());
    }

    [Fact]
    public async Task Refuses_value_reads_on_a_secure_field_with_a_POLICY_DENIED_message()
    {
        using var scene = ReferenceScene.Create();
        var screen = scene.Screen;
        var reads = new (string Name, Func<Task> Run)[]
        {
            ("textContent on a secure field is denied", () => screen.GetByLabel("Password").TextContentAsync()),
            ("inputValue on a secure field is denied", () => screen.GetByLabel("Password").InputValueAsync()),
            ("getAttribute on a secure field is denied", () => screen.GetByLabel("Password").GetAttributeAsync("type")),
            ("allTextContents over a secure field is denied", () => screen.GetByRole("textbox").AllTextContentsAsync()),
        };

        foreach (var (name, run) in reads)
        {
            var error = await Assert.ThrowsAsync<TestException>(run);
            Assert.True(error.Code == "POLICY_DENIED", name + ": " + error.Code + " " + error.Message);
            Assert.Contains("reading values from a secure field is denied", error.Message, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Rejects_a_null_filter_and_a_negative_index_before_any_query_runs_and_reports_an_action_before_open_as_APP_NOT_OPEN()
    {
        using var scene = ReferenceScene.Create();
        var todos = scene.Screen.GetByTestId("todo");
        Assert.Throws<ArgumentNullException>(() => todos.Filter((TextMatch)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => todos.Nth(-1));

        using var closed = ReferenceScene.Create(open: false);
        var error = await Assert.ThrowsAsync<TestException>(() =>
            closed.Screen.GetByRole("button", "Submit").TapAsync(new ActionOptions { Timeout = TimeSpan.FromMilliseconds(200) }));
        Assert.Equal("APP_NOT_OPEN", error.Code);
        Assert.Contains("no app is open", error.Message, StringComparison.Ordinal);
    }
}
