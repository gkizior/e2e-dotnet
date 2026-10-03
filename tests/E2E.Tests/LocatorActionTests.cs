// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests;

public sealed class LocatorActionTests
{
    [Fact]
    public async Task Click_is_an_alias_of_tap()
    {
        var taps = 0;
        var world = new DocumentWorld().Map("/", page => page.Button("Save", () => taps++));
        var error = await RunAsync(world, screen => screen.GetByRole("button", "Save").ClickAsync());

        Assert.Null(error);
        Assert.Equal(1, taps);
    }

    [Fact]
    public async Task Double_tap_taps_twice()
    {
        var taps = 0;
        var world = new DocumentWorld().Map("/", page => page.Button("Like", () => taps++));
        var error = await RunAsync(world, screen => screen.GetByRole("button", "Like").DoubleTapAsync());

        Assert.Null(error);
        Assert.Equal(2, taps);
    }

    [Fact]
    public async Task Press_sequentially_appends_to_the_current_value()
    {
        var world = new DocumentWorld().Map("/", page => page.Textbox("Search", "a"));
        var error = await RunAsync(world, async screen =>
        {
            var search = screen.GetByRole("textbox", "Search");
            await search.PressSequentiallyAsync("bc", new PressSequentiallyOptions { Delay = TimeSpan.FromMilliseconds(1) });
            Assert.Equal("abc", await search.InputValueAsync());
        });

        Assert.Null(error);
    }

    [Fact]
    public async Task Press_sequentially_rejects_a_node_that_does_not_accept_text()
    {
        var world = new DocumentWorld().Map("/", page => page.Button("Save"));
        var error = await RunAsync(world, screen => screen.GetByRole("button", "Save").PressSequentiallyAsync("x"));

        Assert.Equal("NOT_ACTIONABLE", Assert.IsType<EngineException>(error).Code);
    }

    [Fact]
    public async Task Select_option_sets_the_control_value()
    {
        var world = new DocumentWorld().Map("/", page => page.TestId("plan", "combobox", "Plan"));
        var error = await RunAsync(world, async screen =>
        {
            var plan = screen.GetByRole("combobox", "Plan");
            await plan.SelectOptionAsync("pro");
            Assert.Equal("pro", await plan.InputValueAsync());
        });

        Assert.Null(error);
    }

    [Fact]
    public async Task Fill_accepts_a_secret_and_keeps_it_out_of_the_snapshot()
    {
        DocumentElement? box = null;
        var world = new DocumentWorld().Map("/", page => box = page.Textbox("Password", secure: true));
        var error = await RunAsync(world, async screen =>
        {
            var password = screen.GetByRole("textbox", "Password");
            await password.FillAsync(Secret.Create("password", "hunter2"));
            Assert.Null(await password.InputValueAsync());
        });

        Assert.Null(error);
        Assert.Equal("hunter2", box?.Value);
    }

    [Fact]
    public async Task Focus_moves_keyboard_input_to_the_node()
    {
        var taps = 0;
        var world = new DocumentWorld().Map("/", page => page.Button("Save", () => taps++));
        var error = await RunAsync(world, async screen =>
        {
            var save = screen.GetByRole("button", "Save");
            await save.ScrollIntoViewAsync();
            await save.FocusAsync();
            await save.PressAsync("Enter");
        });

        Assert.Null(error);
        Assert.Equal(1, taps);
    }

    [Fact]
    public async Task Focus_waits_for_an_enabled_node()
    {
        var world = new DocumentWorld().Map("/", page => page.Button("Save").Disabled = true);
        var error = await RunAsync(world, screen => screen.GetByRole("button", "Save").FocusAsync());

        Assert.Equal("NOT_ACTIONABLE", Assert.IsType<TestException>(error).Code);
    }

    private static async Task<Exception?> RunAsync(DocumentWorld world, Func<Screen, Task> body)
    {
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(world),
            BaseUrl = "https://app.test",
            TestTitle = "locator > action",
            AssertionTimeout = TimeSpan.FromSeconds(1),
            ActionTimeout = TimeSpan.FromMilliseconds(200),
            TestTimeout = TimeSpan.FromSeconds(10),
            StepTimeout = TimeSpan.FromSeconds(5),
        });
        Exception? error = null;
        try
        {
            await session.App.OpenAsync("/");
            await body(session.Screen);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);
        return error;
    }
}
