// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests;

public sealed class FixtureTests
{
    [Fact]
    public async Task App_exposes_the_base_url_and_the_context_the_platform()
    {
        await using var session = await StartAsync();
        Assert.Equal("https://billing.test", session.App.BaseUrl);
        Assert.Equal("document", session.Context.Platform);
        Assert.Same(session.Browser, session.Context.Browser);
        session.Complete();
    }

    [Fact]
    public async Task App_back_returns_to_the_previous_route()
    {
        await using var session = await StartAsync();
        await session.App.OpenAsync("/settings/billing");
        await session.Screen.GetByRole("link", "Invoices").TapAsync();
        await Expect.That(session.Screen.GetByRole("heading", "Invoices")).ToBeVisibleAsync();

        await session.App.BackAsync();
        await Expect.That(session.Screen.GetByRole("heading", "Billing")).ToBeVisibleAsync();

        // With no earlier entry, back does nothing.
        await session.App.BackAsync();
        await Expect.That(session.Screen.GetByRole("heading", "Billing")).ToBeVisibleAsync();
        session.Complete();
    }

    [Fact]
    public async Task App_restart_and_clear_state_leave_a_blank_page_with_no_history()
    {
        await using var session = await StartAsync();
        await session.App.OpenAsync("/settings/billing");
        await session.App.RestartAsync();
        var blank = await Assert.ThrowsAsync<EngineException>(() => session.Screen.GetByRole("heading", "Billing").TapAsync());
        Assert.Equal("NOT_FOUND", blank.Code);

        await session.App.OpenAsync("/settings/invoices");
        await session.App.ClearStateAsync();
        await session.App.BackAsync();
        await session.App.OpenAsync("/settings/billing");
        await Expect.That(session.Screen.GetByRole("heading", "Billing")).ToBeVisibleAsync();
        session.Complete();
    }

    [Fact]
    public async Task Browser_is_unsupported_on_the_document_engine()
    {
        await using var session = await StartAsync();
        await session.App.OpenAsync("/settings/billing");
        var error = await Assert.ThrowsAsync<EngineException>(() => session.Browser.ReloadAsync());
        Assert.Equal("UNSUPPORTED_CAPABILITY", error.Code);
        Assert.Contains("browser.reload", error.Message, StringComparison.Ordinal);
        Assert.Contains("document", error.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<EngineException>(() => session.Browser.UrlAsync());
        await Assert.ThrowsAsync<EngineException>(() => session.Browser.Keyboard.PressAsync("Enter"));
        await Assert.ThrowsAsync<EngineException>(() => session.Browser.Mouse.DownAsync());
        session.Complete();
    }

    [Fact]
    public async Task A_custom_engine_without_lifecycle_support_fails_clearly()
    {
        await using var session = await E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new BareEngine(),
            BaseUrl = "https://billing.test",
            TestTitle = "bare",
            CacheEnabled = false,
        });
        var error = await Assert.ThrowsAsync<EngineException>(() => session.App.RestartAsync());
        Assert.Equal("UNSUPPORTED_CAPABILITY", error.Code);
        Assert.Contains("app.restart", error.Message, StringComparison.Ordinal);
        session.Complete();
    }

    private static Task<E2ESession> StartAsync()
    {
        var world = new DocumentWorld()
            .Map("/settings/billing", page =>
            {
                page.Heading("Billing");
                page.Link("Invoices", "/settings/invoices");
            })
            .Map("/settings/invoices", page => page.Heading("Invoices"));
        return E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(world),
            BaseUrl = "https://billing.test",
            TestTitle = "fixtures",
            CacheEnabled = false,
        });
    }

    private sealed class BareEngine : IEngine
    {
        public string Platform => "bare";

        public string Version => "1.0";

        public EngineCapabilities Capabilities => EngineCapabilities.None;

        public Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<IEngineSession>(new BareSession());
    }

    private sealed class BareSession : IEngineSession
    {
        public string Route => "/";

        public Task OpenAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Observation> ObserveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new Observation { Route = "/", Roots = [] });

        public Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PressAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
