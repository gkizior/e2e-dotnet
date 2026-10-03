// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using E2E.Engine;

namespace E2E.Tests;

public sealed class LocatorReadTests
{
    [Fact]
    public async Task Reads_report_the_current_state_of_one_match()
    {
        var world = new DocumentWorld().Map("/form", page =>
        {
            var link = page.Link("Docs", "/docs");
            link.Attributes["href"] = "/docs";
            link.Rect = new BoundingBox(10, 20, 30, 40);
            page.Button("Save").Disabled = true;
            page.Checkbox("Remember me", isChecked: true);
            page.Status("Saved", hidden: true);
        });

        await RunAsync(world, "/form", async screen =>
        {
            var link = screen.GetByRole("link", "Docs");
            Assert.Equal("/docs", await link.GetAttributeAsync("href"));
            Assert.Null(await link.GetAttributeAsync("target"));
            Assert.Equal(new BoundingBox(10, 20, 30, 40), await link.BoundingBoxAsync());
            Assert.Null(await screen.GetByRole("button", "Save").BoundingBoxAsync());

            Assert.True(await link.IsVisibleAsync());
            Assert.False(await link.IsHiddenAsync());
            Assert.False(await screen.GetByRole("status", "Saved").IsVisibleAsync());
            Assert.True(await screen.GetByRole("status", "Saved").IsHiddenAsync());
            Assert.True(await screen.GetByRole("button", "Missing").IsHiddenAsync());

            Assert.True(await screen.GetByRole("button", "Save").IsDisabledAsync());
            Assert.False(await screen.GetByRole("button", "Save").IsEnabledAsync());
            Assert.True(await link.IsEnabledAsync());
            Assert.True(await screen.GetByRole("checkbox", "Remember me").IsCheckedAsync());
        });
    }

    [Fact]
    public async Task Secure_fields_never_report_a_value_attribute()
    {
        var world = new DocumentWorld().Map("/login", page =>
        {
            var password = page.Textbox("Password", "hunter2", secure: true);
            password.Attributes["value"] = "hunter2";
            password.Attributes["type"] = "password";
        });

        await RunAsync(world, "/login", async screen =>
        {
            var field = screen.GetByRole("textbox", "Password");
            Assert.Null(await field.GetAttributeAsync("value"));
            Assert.Equal("password", await field.GetAttributeAsync("type"));
        });
    }

    [Fact]
    public async Task Count_all_and_all_text_contents_read_every_match_without_waiting()
    {
        var world = new DocumentWorld().Map("/list", page =>
        {
            page.Button("Alpha");
            page.Button("Beta");
            page.Button("Gamma").Hidden = true;
        });

        await RunAsync(world, "/list", async screen =>
        {
            var buttons = screen.GetByRole("button");
            Assert.Equal(2, await buttons.CountAsync());
            Assert.Equal(["Alpha", "Beta"], await buttons.AllTextContentsAsync());
            var all = await buttons.AllAsync();
            Assert.Equal(2, all.Count);
            Assert.Equal("Beta", await all[1].TextContentAsync());

            var missing = screen.GetByRole("link");
            var watch = Stopwatch.StartNew();
            Assert.Equal(0, await missing.CountAsync());
            Assert.Empty(await missing.AllAsync());
            Assert.Empty(await missing.AllTextContentsAsync());
            Assert.True(watch.Elapsed < TimeSpan.FromMilliseconds(250));
        });
    }

    [Fact]
    public async Task Single_node_reads_are_strict_and_fail_when_nothing_matches()
    {
        var world = new DocumentWorld().Map("/dup", page =>
        {
            page.Button("Save");
            page.Button("Save");
        });

        await RunAsync(world, "/dup", async screen =>
        {
            var strict = await Assert.ThrowsAsync<TestException>(() => screen.GetByRole("button", "Save").IsEnabledAsync());
            Assert.Equal("STRICT_MODE", strict.Code);
            strict = await Assert.ThrowsAsync<TestException>(() => screen.GetByRole("button", "Save").IsVisibleAsync());
            Assert.Equal("STRICT_MODE", strict.Code);
            var missing = await Assert.ThrowsAsync<TestException>(() => screen.GetByRole("button", "Missing").GetAttributeAsync("id"));
            Assert.Equal("NOT_FOUND", missing.Code);
        });
    }

    [Fact]
    public async Task Wait_for_visible_waits_until_the_node_appears()
    {
        DocumentElement? status = null;
        var world = new DocumentWorld().Map("/wait", page => status = page.Status("Ready", hidden: true));

        await RunAsync(world, "/wait", async screen =>
        {
            var locator = screen.GetByRole("status", "Ready");
            await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForState.Attached });
            _ = Task.Run(async () =>
            {
                await Task.Delay(80);
                status!.Hidden = false;
            });
            await locator.WaitForAsync(new LocatorWaitForOptions { Timeout = TimeSpan.FromSeconds(2) });
            Assert.True(await locator.IsVisibleAsync());
        });
    }

    [Fact]
    public async Task Wait_for_hidden_and_detached()
    {
        DocumentElement? status = null;
        var world = new DocumentWorld().Map("/wait", page =>
        {
            status = page.Status("Ready");
            page.Button("Close", () => page.Roots.RemoveAt(0));
        });

        await RunAsync(world, "/wait", async screen =>
        {
            var locator = screen.GetByRole("status", "Ready");
            await screen.GetByRole("status", "Missing").WaitForAsync(new LocatorWaitForOptions { State = WaitForState.Hidden });
            _ = Task.Run(async () =>
            {
                await Task.Delay(50);
                status!.Hidden = true;
            });
            await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForState.Hidden, Timeout = TimeSpan.FromSeconds(2) });

            var stillAttached = await Assert.ThrowsAsync<TestException>(() =>
                locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForState.Detached, Timeout = TimeSpan.FromMilliseconds(100) }));
            Assert.Equal("TIMEOUT", stillAttached.Code);

            await screen.GetByRole("button", "Close").TapAsync();
            await locator.WaitForAsync(new LocatorWaitForOptions { State = WaitForState.Detached, Timeout = TimeSpan.FromSeconds(2) });
        });
    }

    [Fact]
    public async Task Wait_for_times_out_with_the_option_and_rejects_many_matches()
    {
        var world = new DocumentWorld().Map("/dup", page =>
        {
            page.Button("Save");
            page.Button("Save");
        });

        await RunAsync(world, "/dup", async screen =>
        {
            var watch = Stopwatch.StartNew();
            var timeout = await Assert.ThrowsAsync<TestException>(() =>
                screen.GetByRole("button", "Missing").WaitForAsync(new LocatorWaitForOptions { Timeout = TimeSpan.FromMilliseconds(100) }));
            Assert.Equal("TIMEOUT", timeout.Code);
            Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(100));
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4));

            var strict = await Assert.ThrowsAsync<TestException>(() => screen.GetByRole("button", "Save").WaitForAsync());
            Assert.Equal("STRICT_MODE", strict.Code);
        });
    }

    [Fact]
    public async Task Per_action_timeout_overrides_the_configured_action_timeout()
    {
        var world = new DocumentWorld().Map("/late", page => page.Button("Save").Disabled = true);

        await RunAsync(world, "/late", async screen =>
        {
            var save = screen.GetByRole("button", "Save");
            var watch = Stopwatch.StartNew();
            var error = await Assert.ThrowsAsync<TestException>(() => save.TapAsync(new ActionOptions { Timeout = TimeSpan.FromMilliseconds(100) }));
            Assert.Equal("NOT_ACTIONABLE", error.Code);
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(4));

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => save.TapAsync(new ActionOptions { Timeout = TimeSpan.FromMilliseconds(-1) }));
        });
    }

    private static async Task RunAsync(DocumentWorld world, string route, Func<Screen, Task> body)
    {
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(world),
            BaseUrl = "https://app.test",
            CacheEnabled = false,
            TestTitle = "locator > reads",
            AssertionTimeout = TimeSpan.FromSeconds(2),
            ActionTimeout = TimeSpan.FromSeconds(5),
            TestTimeout = TimeSpan.FromSeconds(20),
            StepTimeout = TimeSpan.FromSeconds(5),
        });
        Exception? error = null;
        try
        {
            await session.App.OpenAsync(route);
            await body(session.Screen);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);
        if (error is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }
}
