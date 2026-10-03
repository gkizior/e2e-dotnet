// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests;

public sealed class ConfigTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "e2e-config-root");

    [Fact]
    public void Parses_the_upstream_shape()
    {
        var config = Parse("""
            {
              "targets": [{ "name": "site", "platform": "web", "app": { "url": "http://127.0.0.1:4173" } }],
              "timeout": 90000,
              "launchTimeout": 20000,
              "actionTimeout": 7000,
              "assertionTimeout": 3000,
              "cleanupTimeout": 4000,
              "retries": 2,
              "agents": { "default": { "model": "gpt-4.1-mini", "baseUrl": "http://localhost:11434/v1", "apiKeyEnv": "LOCAL_KEY", "maxModelCalls": 8 } },
              "cache": { "mode": "read-only", "dir": "recordings", "strict": true },
              "secrets": { "stripe-key": "sk_test_123456" }
            }
            """);

        Assert.Equal("site", config.Target.Name);
        Assert.Equal("http://127.0.0.1:4173", config.Target.App.Url);
        Assert.Equal(TimeSpan.FromSeconds(90), config.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(20), config.LaunchTimeout);
        Assert.Equal(TimeSpan.FromSeconds(7), config.ActionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(3), config.AssertionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(4), config.CleanupTimeout);
        Assert.Equal(2, config.Retries);
        Assert.Equal("gpt-4.1-mini", config.Agent.Model);
        Assert.Equal("http://localhost:11434/v1", config.Agent.BaseUrl);
        Assert.Equal("LOCAL_KEY", config.Agent.ApiKeyEnv);
        Assert.Equal(8, config.Agent.MaxModelCalls);
        Assert.Equal(CacheMode.ReadOnly, config.Cache.Mode);
        Assert.Equal(Path.Combine(Root, "recordings"), config.Cache.Directory);
        Assert.True(config.Cache.Strict);
        Assert.Equal("<secret:stripe-key>", config.Secrets.Get("stripe-key").ToString());
        Assert.IsType<OpenAiCompatibleModel>(config.Agent.CreateModel());
    }

    [Fact]
    public void Defaults_match_upstream()
    {
        var config = Parse("{}");

        Assert.Equal("web", config.Target.Platform);
        Assert.Null(config.Target.App.Url);
        Assert.Equal(TimeSpan.FromSeconds(120), config.Timeout);
        Assert.Equal(TimeSpan.FromSeconds(60), config.LaunchTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), config.ActionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(5), config.AssertionTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), config.CleanupTimeout);
        Assert.Equal(0, config.Retries);
        Assert.Null(config.Agent.CreateModel());
        Assert.Equal(CacheMode.ReadWrite, config.Cache.Mode);
        Assert.Equal(Path.Combine(Root, ".e2e", "cache"), config.Cache.Directory);
        Assert.False(config.Cache.Strict);
        Assert.False(config.Ci);
    }

    [Fact]
    public void Ci_reads_the_cache_and_retries_once_unless_configured()
    {
        var ci = Env(("CI", "true"));
        var defaults = Parse("{}", ci);
        Assert.True(defaults.Ci);
        Assert.Equal(CacheMode.ReadOnly, defaults.Cache.Mode);
        Assert.Equal(1, defaults.Retries);

        var cacheWithoutMode = Parse("""{ "cache": { "strict": true } }""", ci);
        Assert.Equal(CacheMode.ReadOnly, cacheWithoutMode.Cache.Mode);

        var explicitConfig = Parse("""{ "cache": { "mode": "read-write" }, "retries": 0 }""", ci);
        Assert.Equal(CacheMode.ReadWrite, explicitConfig.Cache.Mode);
        Assert.Equal(0, explicitConfig.Retries);

        Assert.False(Parse("{}", Env(("CI", "false"))).Ci);
        Assert.False(Parse("{}", Env(("CI", "0"))).Ci);
    }

    [Theory]
    [InlineData("""{ "timeot": 1000 }""", "unknown config key \"timeot\"; did you mean \"timeout\"?")]
    [InlineData("""{ "app": { "url": "http://x" } }""", "unknown config key \"app\"; the app under test is declared on its target")]
    [InlineData("""{ "agent": { "model": "m" } }""", "unknown config key \"agent\"; agents are named")]
    [InlineData("""{ "timeouts": { "testMs": 1 } }""", "unknown config key \"timeouts\"; timeouts are top-level milliseconds")]
    [InlineData("""{ "workers": 2 }""", "config key \"workers\" is not supported by the .NET port")]
    [InlineData("""{ "cache": { "mod": "off" } }""", "unknown cache key \"mod\"; did you mean \"mode\"?")]
    [InlineData("""{ "cache": { "mode": "on" } }""", "cache.mode must be \"off\", \"read-only\", or \"read-write\"")]
    [InlineData("""{ "cache": { "strict": "yes" } }""", "cache.strict must be a boolean")]
    [InlineData("""{ "targets": [] }""", "targets must be a non-empty array")]
    [InlineData("""{ "targets": [{ "app": { "url": "http://x", "port": 1 } }] }""", "unknown targets[0].app key \"port\"")]
    [InlineData("""{ "targets": [{ "platform": "ios" }] }""", "targets[0].platform \"ios\" is not supported")]
    [InlineData("""{ "agents": { "default": { "modle": "m" } } }""", "unknown agents.default key \"modle\"; did you mean \"model\"?")]
    [InlineData("""{ "agents": { "default": { "maxSteps": 3 } } }""", "agents.default key \"maxSteps\" is not supported by the .NET port")]
    [InlineData("""{ "timeout": 0 }""", "timeout must be a positive integer")]
    [InlineData("""{ "retries": 11 }""", "retries must be an integer from 0 to 10")]
    [InlineData("""{ "secrets": { "pin": "12345" } }""", "secret \"pin\" must be at least 6 characters")]
    [InlineData("""{ "secrets": { "token": null } }""", "secret \"token\" has no value; set E2E_SECRET_TOKEN")]
    [InlineData("""{ "secrets": { "api-key": "aaaaaa", "api_key": "bbbbbb" } }""", "names share override variables")]
    [InlineData("""[]""", "the config must be a JSON object")]
    public void Rejects_an_invalid_config(string json, string message)
    {
        var error = Assert.Throws<ConfigurationException>(() => Parse(json));
        Assert.Equal("INVALID_CONFIG", error.Code);
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Secrets_read_the_override_variable_first()
    {
        var config = Parse(
            """{ "secrets": { "stripe-key": "from-config", "token": null } }""",
            Env(("E2E_SECRET_STRIPE_KEY", "from-environment"), ("E2E_SECRET_TOKEN", "token-value")));

        Assert.Equal("E2E_SECRET_STRIPE_KEY", Secrets.EnvironmentVariable("stripe-key"));
        Assert.Equal("E2E_SECRET_A_B_C", Secrets.EnvironmentVariable("a.b c"));
        Assert.Equal(
            ["stripe-key", "token"],
            config.Secrets.Names.Order(StringComparer.Ordinal).ToArray());
        var error = Assert.Throws<TestException>(() => config.Secrets.Get("missing"));
        Assert.Equal("SECRET_UNAVAILABLE", error.Code);
    }

    [Fact]
    public void Find_walks_up_from_each_start_directory()
    {
        var root = Path.Combine(Path.GetTempPath(), "e2e-config", Guid.NewGuid().ToString("n"));
        var nested = Path.Combine(root, "bin", "Debug", "net10.0");
        Directory.CreateDirectory(nested);
        try
        {
            Assert.Null(E2EConfig.Find(nested));
            var path = Path.Combine(root, E2EConfig.FileName);
            File.WriteAllText(path, """{ "targets": [{ "app": { "url": "https://app.test" } }], "cache": { "dir": "cache" } }""");

            Assert.Equal(path, E2EConfig.Find(Path.Combine(root, "missing-first"), nested));
            var config = E2EConfig.Load(path, Env());
            Assert.Equal(path, config.ConfigPath);
            Assert.Equal(root, config.ProjectRoot);
            Assert.Equal("https://app.test", config.Target.App.Url);
            Assert.Equal(Path.Combine(root, "cache"), config.Cache.Directory);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Read_only_mode_replays_without_writing()
    {
        var directory = TempCache();
        var calls = 0;
        await using (var session = await StartAsync(directory, CacheMode.ReadOnly, strict: false, "Upgrade to Pro", () => calls++))
        {
            await UpgradeAsync(session);
            session.Complete();
        }

        Assert.True(calls > 0);
        Assert.False(Directory.Exists(directory));

        await using (var session = await StartAsync(directory, CacheMode.ReadWrite, strict: false, "Upgrade to Pro", () => { }))
        {
            await UpgradeAsync(session);
            session.Complete();
        }

        var recorded = Directory.GetFiles(directory).Length;
        calls = 0;
        await using (var session = await StartAsync(directory, CacheMode.ReadOnly, strict: false, "Upgrade to Pro", () => calls++))
        {
            await session.App.OpenAsync("/settings/billing");
            await session.Agent.ActAsync("upgrade the workspace to the Pro plan");
            Assert.Equal(1, session.Replayed);
            session.Complete(new TestException("TEST_FAILED", "a read-only failure deletes nothing"));
        }

        Assert.Equal(0, calls);
        Assert.Equal(recorded, Directory.GetFiles(directory).Length);
    }

    [Fact]
    public async Task Strict_mode_fails_a_stale_recording()
    {
        var directory = TempCache();
        await using (var session = await StartAsync(directory, CacheMode.ReadWrite, strict: true, "Upgrade to Pro", () => { }))
        {
            await UpgradeAsync(session);
            session.Complete();
        }

        var calls = 0;
        await using (var session = await StartAsync(directory, CacheMode.ReadWrite, strict: true, "Upgrade now", () => calls++))
        {
            await session.App.OpenAsync("/settings/billing");
            var error = await Assert.ThrowsAsync<AgentException>(() => session.Agent.ActAsync("upgrade the workspace to the Pro plan"));
            Assert.Equal("REPLAY_STALE", error.Code);
            session.Complete(error);
        }

        Assert.Equal(0, calls);
        Assert.NotEmpty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Launch_timeout_stops_a_slow_engine()
    {
        var error = await Assert.ThrowsAsync<EngineException>(() => E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new HangingEngine(),
            TestTitle = "launch",
            LaunchTimeout = TimeSpan.FromMilliseconds(50),
        }));
        Assert.Equal("ENVIRONMENT_UNAVAILABLE", error.Code);
        Assert.Contains("launch timeout", error.Message, StringComparison.Ordinal);
    }

    private static E2EConfig Parse(string json, Func<string, string?>? environment = null)
    {
        return E2EConfig.Parse(json, Root, environment ?? Env());
    }

    private static Func<string, string?> Env(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }

    private static async Task UpgradeAsync(E2ESession session)
    {
        await session.App.OpenAsync("/settings/billing");
        await session.Agent.ActAsync("upgrade the workspace to the Pro plan");
        await session.Agent.AssertAsync("the invoice preview shows a prorated amount");
    }

    private static Task<E2ESession> StartAsync(string directory, CacheMode mode, bool strict, string button, Action onAct)
    {
        return E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(BillingWorld.Create(button)),
            Model = new ScriptedModel(request =>
            {
                var text = string.Join('\n', request.Messages.Select(message => message.Content));
                if (text.Contains("Statement:", StringComparison.Ordinal))
                {
                    return text.Contains("Prorated", StringComparison.Ordinal)
                        ? ModelResponses.Done("passed", "The invoice is prorated.")
                        : ModelResponses.Done("failed", "No prorated amount.", "ASSERTION_FAILED");
                }

                onAct();
                return text.Contains("tapped", StringComparison.Ordinal)
                    ? ModelResponses.Done("passed", "Upgraded to Pro.")
                    : ModelResponses.Tap("button", button);
            }),
            BaseUrl = "https://billing.test",
            Cache = new FileStepCache(directory),
            CacheMode = mode,
            CacheStrict = strict,
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            TestTitle = "billing > upgrades",
        });
    }

    private static string TempCache() => Path.Combine(Path.GetTempPath(), "e2e-config-cache", Guid.NewGuid().ToString("n"));

    private sealed class HangingEngine : IEngine
    {
        public string Platform => "web";

        public string Version => "1.0";

        public EngineCapabilities Capabilities => EngineCapabilities.None;

        public async Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }
}
