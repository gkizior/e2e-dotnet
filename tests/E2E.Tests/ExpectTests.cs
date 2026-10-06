// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Tests;

public sealed class ExpectTests
{
    [Fact]
    public async Task Not_passes_after_a_second_of_continuous_truth()
    {
        var watch = Stopwatch.StartNew();
        var error = await RunAsync(screen => Expect.That(screen.GetByRole("status", "Missing")).Not.ToBeVisibleAsync());

        Assert.Null(error);
        Assert.True(watch.Elapsed >= TimeSpan.FromMilliseconds(900), "negation passed after " + watch.Elapsed);
    }

    [Fact]
    public async Task Not_fails_while_the_condition_holds()
    {
        var error = await RunAsync(
            screen => Expect.That(screen.GetByRole("heading", "Billing")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(300)));

        var failure = Assert.IsType<TestException>(error);
        Assert.Equal("ASSERTION_FAILED", failure.Code);
        Assert.Contains("not.toBeVisible", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Not_restarts_its_clock_when_the_condition_flips_back()
    {
        DocumentElement? status = null;
        var world = new DocumentWorld().Map("/flip", page => status = page.Status("Ready"));
        var error = await RunAsync(
            async screen =>
            {
                _ = Task.Run(async () =>
                {
                    await Task.Delay(100);
                    status!.Hidden = true;
                    await Task.Delay(400);
                    status.Hidden = false;
                });
                await Expect.That(screen.GetByRole("status", "Ready")).Not.ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(1500));
            },
            world,
            "/flip");

        Assert.Equal("ASSERTION_FAILED", Assert.IsType<TestException>(error).Code);
    }

    [Fact]
    public async Task Not_on_a_state_matcher_needs_a_node()
    {
        var error = await RunAsync(
            screen => Expect.That(screen.GetByRole("checkbox", "Missing")).Not.ToBeCheckedAsync(timeout: TimeSpan.FromMilliseconds(300)));

        Assert.Equal("ASSERTION_FAILED", Assert.IsType<TestException>(error).Code);
    }

    [Fact]
    public async Task A_matcher_timeout_replaces_the_assertion_timeout()
    {
        var watch = Stopwatch.StartNew();
        var error = await RunAsync(
            screen => Expect.That(screen.GetByRole("status", "Missing")).ToBeVisibleAsync(timeout: TimeSpan.FromMilliseconds(100)),
            assertionTimeout: TimeSpan.FromSeconds(10));

        Assert.Equal("ASSERTION_FAILED", Assert.IsType<TestException>(error).Code);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Text_matchers_take_a_regex_and_ignore_case()
    {
        var error = await RunAsync(async screen =>
        {
            var invoice = screen.GetByText("Invoice preview");
            await Expect.That(invoice).ToHaveTextAsync(new Regex("^Invoice"));
            await Expect.That(invoice).ToHaveTextAsync("invoice PREVIEW", ignoreCase: true);
            await Expect.That(invoice).ToContainTextAsync("PREVIEW", ignoreCase: true);
            await Expect.That(invoice).ToContainTextAsync(new Regex("PREV"), ignoreCase: true);
            await Expect.That(invoice).Not.ToHaveTextAsync(new Regex("invoice", RegexOptions.IgnoreCase), ignoreCase: false, timeout: TimeSpan.FromMilliseconds(100));
        });

        Assert.Null(error);
    }

    [Fact]
    public async Task ToHaveText_without_ignore_case_compares_case()
    {
        var error = await RunAsync(
            screen => Expect.That(screen.GetByText("Invoice preview")).ToHaveTextAsync("invoice preview", timeout: TimeSpan.FromMilliseconds(100)));

        var failure = Assert.IsType<TestException>(error);
        Assert.Contains("observed text \"Invoice preview\"", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Text_matchers_take_a_list()
    {
        var world = new DocumentWorld().Map("/list", page =>
        {
            page.Button("Alpha");
            page.Button("Beta");
            page.Button("Gamma");
        });
        var error = await RunAsync(
            async screen =>
            {
                var buttons = screen.GetByRole("button");
                await Expect.That(buttons).ToHaveTextAsync(["Alpha", new Regex("^Be"), "Gamma"]);
                await Expect.That(buttons).ToContainTextAsync(["lph", "amm"]);
                await Expect.That(buttons).Not.ToHaveTextAsync(["Alpha", "Beta"], timeout: TimeSpan.FromMilliseconds(100));
                await Expect.That(buttons).Not.ToContainTextAsync(["amm", "lph"], timeout: TimeSpan.FromMilliseconds(100));
            },
            world,
            "/list");

        Assert.Null(error);
    }

    [Fact]
    public async Task ToBeAttached_counts_hidden_nodes()
    {
        var error = await RunAsync(async screen =>
        {
            await Expect.That(screen.GetByRole("status", "Pro")).ToBeAttachedAsync();
            await Expect.That(screen.GetByRole("status", "Pro")).ToBeHiddenAsync();
            await Expect.That(screen.GetByRole("status", "Missing")).ToBeAttachedAsync(attached: false);
        });

        Assert.Null(error);
    }

    [Fact]
    public async Task State_matchers_read_selected_expanded_and_focused()
    {
        var world = new DocumentWorld().Map("/states", page =>
        {
            page.TestId("tab", "tab", "Overview").Selected = true;
            page.TestId("menu", "button", "Menu").Expanded = true;
            page.Textbox("Email").Focused = true;
            page.Textbox("Name");
        });
        var error = await RunAsync(
            async screen =>
            {
                await Expect.That(screen.GetByRole("tab", "Overview")).ToBeSelectedAsync();
                await Expect.That(screen.GetByRole("button", "Menu")).ToBeExpandedAsync();
                await Expect.That(screen.GetByLabel("Email")).ToBeFocusedAsync();
                await Expect.That(screen.GetByLabel("Name")).Not.ToBeFocusedAsync(timeout: TimeSpan.FromMilliseconds(100));
            },
            world,
            "/states");

        Assert.Null(error);
    }

    [Fact]
    public async Task Negation_passes_when_false_for_a_short_budget_on_a_slow_engine()
    {
        var world = new DocumentWorld().Map("/states", page => page.Textbox("Name"));
        var error = await RunAsync(
            screen => Expect.That(screen.GetByLabel("Name")).Not.ToBeFocusedAsync(timeout: TimeSpan.FromMilliseconds(100)),
            world,
            "/states",
            engine: new SlowEngine(new DocumentEngine(world), TimeSpan.FromMilliseconds(150)));

        Assert.Null(error);
    }

    [Fact]
    public async Task Boolean_flags_flip_the_matcher()
    {
        var world = new DocumentWorld().Map("/flags", page =>
        {
            page.Button("Save").Disabled = true;
            page.Checkbox("Remember me");
            page.Status("Saved", hidden: true);
        });
        var error = await RunAsync(
            async screen =>
            {
                await Expect.That(screen.GetByRole("button", "Save")).ToBeEnabledAsync(enabled: false);
                await Expect.That(screen.GetByRole("checkbox", "Remember me")).ToBeCheckedAsync(isChecked: false);
                await Expect.That(screen.GetByRole("status", "Saved")).ToBeVisibleAsync(visible: false);
            },
            world,
            "/flags");

        Assert.Null(error);
    }

    [Fact]
    public async Task ToHaveAttribute_and_ToHaveAccessibleName()
    {
        var world = new DocumentWorld().Map("/attrs", page =>
        {
            var link = page.Link("Docs", "/docs");
            link.Attributes["href"] = "/docs";
            link.Attributes["target"] = "_blank";
        });
        var error = await RunAsync(
            async screen =>
            {
                var link = screen.GetByRole("link");
                await Expect.That(link).ToHaveAttributeAsync("href");
                await Expect.That(link).ToHaveAttributeAsync("target", "_BLANK", ignoreCase: true);
                await Expect.That(link).ToHaveAttributeAsync("href", new Regex("^/doc"));
                await Expect.That(link).Not.ToHaveAttributeAsync("rel", timeout: TimeSpan.FromMilliseconds(100));
                await Expect.That(link).ToHaveAccessibleNameAsync("docs", ignoreCase: true);
            },
            world,
            "/attrs");

        Assert.Null(error);
    }

    [Fact]
    public async Task Reading_a_secure_value_is_denied()
    {
        var world = new DocumentWorld().Map("/login", page => page.Textbox("Password", "hunter2", secure: true));
        var error = await RunAsync(
            screen => Expect.That(screen.GetByLabel("Password")).ToHaveValueAsync("", timeout: TimeSpan.FromMilliseconds(100)),
            world,
            "/login");

        Assert.Equal("POLICY_DENIED", Assert.IsType<TestException>(error).Code);
    }

    private static async Task<Exception?> RunAsync(
        Func<Screen, Task> body,
        DocumentWorld? world = null,
        string route = "/settings/billing",
        TimeSpan? assertionTimeout = null,
        IEngine? engine = null)
    {
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = engine ?? new DocumentEngine(world ?? BillingWorld.Create()),
            BaseUrl = "https://billing.test",
            TestTitle = "expect > case",
            AssertionTimeout = assertionTimeout ?? TimeSpan.FromSeconds(2),
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            TestTimeout = TimeSpan.FromSeconds(20),
            StepTimeout = TimeSpan.FromSeconds(5),
        });
        Exception? error = null;
        try
        {
            await session.Context.App.OpenAsync(route);
            await body(session.Context.Screen);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        session.Complete(error);
        return error;
    }

    /// <summary>Slows every observation, as a loaded CI runner does.</summary>
    private sealed class SlowEngine(IEngine inner, TimeSpan delay) : IEngine
    {
        public string Platform => inner.Platform;

        public string Version => inner.Version;

        public EngineCapabilities Capabilities => inner.Capabilities;

        public async Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken) =>
            new SlowSession(await inner.StartAsync(options, cancellationToken), delay);
    }

    private sealed class SlowSession(IEngineSession inner, TimeSpan delay) : IEngineSession
    {
        public string Route => inner.Route;

        public Task OpenAsync(string url, CancellationToken cancellationToken) => inner.OpenAsync(url, cancellationToken);

        public async Task<Observation> ObserveAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);
            return await inner.ObserveAsync(cancellationToken);
        }

        public Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken) => inner.PerformAsync(node, action, cancellationToken);

        public Task PressAsync(string key, CancellationToken cancellationToken) => inner.PressAsync(key, cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
