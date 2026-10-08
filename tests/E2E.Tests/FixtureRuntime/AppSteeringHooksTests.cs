// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.FixtureRuntime;

public sealed class AppSteeringHooksTests
{
    [Fact]
    public async Task Restart_and_clearState_run_the_hook_then_reopen_the_app_at_its_base_URL()
    {
        var calls = new List<string>();
        await using var session = await StartAsync(calls, "http://127.0.0.1:4599");
        await session.App.RestartAsync();
        await session.App.ClearStateAsync();
        Assert.Equal(["restart", "open http://127.0.0.1:4599/", "reset", "open http://127.0.0.1:4599/"], calls);
        session.Complete();
    }

    [Fact]
    public async Task Reopen_nothing_on_a_surface_without_an_address()
    {
        var calls = new List<string>();
        await using var session = await StartAsync(calls, baseUrl: null);
        await session.App.RestartAsync();
        await session.App.ClearStateAsync();
        Assert.Equal(["restart", "reset"], calls);
        session.Complete();
    }

    private static Task<E2ESession> StartAsync(List<string> calls, string? baseUrl)
    {
        return E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new SteerableEngine(calls),
            BaseUrl = baseUrl,
            TestTitle = "steerable",
            CacheEnabled = false,
        });
    }

    private sealed class SteerableEngine(List<string> calls) : IEngine
    {
        public string Platform => "fake";

        public string Version => "1";

        public EngineCapabilities Capabilities => EngineCapabilities.None;

        public Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken) =>
            Task.FromResult<IEngineSession>(new SteerableSession(calls));
    }

    private sealed class SteerableSession(List<string> calls) : IEngineSession
    {
        public string Route => "/";

        public Task OpenAsync(string url, CancellationToken cancellationToken)
        {
            calls.Add("open " + url);
            return Task.CompletedTask;
        }

        public Task<Observation> ObserveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new Observation { Route = "/", Roots = [] });

        public Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PressAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RestartAsync(CancellationToken cancellationToken)
        {
            calls.Add("restart");
            return Task.CompletedTask;
        }

        public Task ClearStateAsync(CancellationToken cancellationToken)
        {
            calls.Add("reset");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
