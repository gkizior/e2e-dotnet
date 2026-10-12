// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.RegularExpressions;
using E2E.Cli.Mcp;
using E2E.Cli.Tests.Helpers;
using E2E.Engine;

namespace E2E.Cli.Tests.McpSessionHost;

/// <summary>
/// The session host in-process on a fake engine: the headed flag reaches the engine, an idle session closes
/// itself, a session ends at its time limit, sessions open side by side up to the limit and hold their slot and
/// config until their attempt is gone, a shutdown cancels sessions still opening and waits for them, and an
/// <c>open_session</c> that fails after the attempt opened tears the attempt down and leaves the host ready for the
/// next one.
/// </summary>
public sealed class SessionHostTests : IDisposable
{
    private const string Url = "http://127.0.0.1:1";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly string _dir = Directory.CreateTempSubdirectory("e2e-mcp-host-").FullName;
    private readonly List<string> _logs = [];
    private readonly List<SessionHost> _hosts = [];

    public void Dispose()
    {
        foreach (var host in _hosts)
        {
            host.Dispose();
        }

        Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public async Task Boots_the_engine_headed_when_asked_and_headless_otherwise()
    {
        var headedValues = new List<bool>();
        var fake = new FakeEngine();
        var headed = Host(fake, headedValues, headed: true);
        Assert.Contains("), headed;", await headed.OpenAsync(new OpenSessionOptions()), StringComparison.Ordinal);
        await headed.CloseAsync("done");
        var headless = Host(fake, headedValues, headed: false);
        await headless.OpenAsync(new OpenSessionOptions());
        await headless.CloseAsync("done");
        Assert.Equal([true, false], headedValues);
        Assert.Equal(2, fake.AttemptsStarted);
        Assert.Equal(2, fake.Disposes);
    }

    [Fact]
    public async Task Lets_open_sessions_headed_override_the_servers_default_either_way_for_the_engine()
    {
        var headedValues = new List<bool>();
        var fake = new FakeEngine();
        var headless = Host(fake, headedValues, headed: false);
        Assert.Contains("), headed;", await headless.OpenAsync(new OpenSessionOptions { Headed = true }), StringComparison.Ordinal);
        await headless.CloseAsync("done");
        var headed = Host(fake, headedValues, headed: true);
        Assert.Contains("), headless;", await headed.OpenAsync(new OpenSessionOptions { Headed = false }), StringComparison.Ordinal);
        await headed.CloseAsync("done");
        Assert.Equal([true, false], headedValues);
    }

    [Fact]
    public async Task Closes_an_idle_session_and_disposes_the_engine()
    {
        var fake = new FakeEngine();
        var idle = Host(fake, idle: TimeSpan.FromMilliseconds(100));
        await idle.OpenAsync(new OpenSessionOptions());
        Assert.True(idle.IsOpen);
        await UntilAsync(() => !idle.IsOpen);
        Assert.Contains(_logs, line => line.Contains("idle for", StringComparison.Ordinal));
        await UntilAsync(() => fake.Disposes == 1);
        Assert.Equal(1, fake.AttemptsStarted);
    }

    [Fact]
    public async Task Ends_a_session_at_its_time_limit_and_disposes_the_engine()
    {
        var fake = new FakeEngine();
        var short_ = Host(fake, lifetime: TimeSpan.FromMilliseconds(500));
        await short_.OpenAsync(new OpenSessionOptions());
        await UntilAsync(() => !short_.IsOpen);
        await UntilAsync(() => _logs.Any(line => line.Contains("reached its time limit", StringComparison.Ordinal)));
        await UntilAsync(() => fake.Disposes == 1);
    }

    [Fact]
    public async Task Opens_sessions_side_by_side_up_to_the_limit_and_a_call_names_its_session_while_several_are_open()
    {
        var pair = Host(new FakeEngine(), maxSessions: 2);
        var opened = await Task.WhenAll(pair.OpenAsync(new OpenSessionOptions()), pair.OpenAsync(new OpenSessionOptions()));
        var first = SessionId(opened[0]);
        var second = SessionId(opened[1]);
        Assert.NotEqual(first, second);
        Assert.Contains($"Pass session \"{first}\" to every tools, call, and close_session; with several sessions open, a call without it fails.", opened[0], StringComparison.Ordinal);
        var full = await Assert.ThrowsAsync<ConfigurationException>(() => pair.OpenAsync(new OpenSessionOptions()));
        Assert.Equal("SESSION_OPEN", full.Code);
        var slots = string.Join(", ", new[] { first, second }.Select(id => $"{id} on \"web\""));
        Assert.Equal($"no session slot is free (e2e mcp --max-sessions 2): {slots}; close_session one first", full.Message);
        var unnamed = Assert.Throws<ConfigurationException>(() => pair.Catalog(null, null));
        Assert.Equal("SESSION_REQUIRED", unnamed.Code);
        Assert.Contains($"2 sessions are open; pass session to name one: {first} on \"web\", {second} on \"web\"", unnamed.Message, StringComparison.Ordinal);
        Assert.Contains($"Session {second} on target \"web\"", pair.Catalog(second, null), StringComparison.Ordinal);
        var unnamedClose = await Assert.ThrowsAsync<ConfigurationException>(() => pair.CloseAsync("done"));
        Assert.Equal("SESSION_REQUIRED", unnamedClose.Code);

        Assert.Contains($"Session {first} closed (done)", await pair.CloseAsync("done", first), StringComparison.Ordinal);
        var ended = Assert.Throws<ConfigurationException>(() => pair.Catalog(first, null));
        Assert.Contains($"session \"{first}\" ended: done; call open_session for a new one", ended.Message, StringComparison.Ordinal);
        Assert.Contains($"Session {second} on target \"web\"", pair.Catalog(null, null), StringComparison.Ordinal);
        Assert.Contains($"Session {second} closed (server shutdown)", await pair.CloseAllAsync("server shutdown"), StringComparison.Ordinal);
        Assert.Null(await pair.CloseAllAsync("server shutdown"));
    }

    [Fact]
    public async Task Counts_a_session_booting_its_first_screen_once_against_the_limit()
    {
        var navigating = new TaskCompletionSource();
        var held = new TaskCompletionSource();
        var calls = 0;
        var fake = new FakeEngine
        {
            OnNavigate = () =>
            {
                if (Interlocked.Increment(ref calls) > 1)
                {
                    return Task.CompletedTask;
                }

                navigating.SetResult();
                return held.Task;
            },
        };
        var pair = Host(fake, maxSessions: 2);
        var first = pair.OpenAsync(new OpenSessionOptions());
        await navigating.Task.WaitAsync(Patience);
        var second = SessionId(await pair.OpenAsync(new OpenSessionOptions()));
        held.SetResult();
        var firstId = SessionId(await first);
        Assert.NotEqual(second, firstId);
        await pair.CloseAsync("done", firstId);
        await pair.CloseAsync("done", second);
    }

    [Fact]
    public async Task Holds_the_slot_and_the_config_of_a_closing_session_until_its_attempt_is_disposed()
    {
        var disposing = new TaskCompletionSource();
        var released = new TaskCompletionSource();
        var fake = new FakeEngine
        {
            OnDispose = () =>
            {
                disposing.TrySetResult();
                return released.Task;
            },
        };
        var single = Host(fake, maxSessions: 1);
        var id = SessionId(await single.OpenAsync(new OpenSessionOptions()));
        var closing = single.CloseAsync("done", id);
        await disposing.Task.WaitAsync(Patience);
        var again = single.CloseAsync("again", id);
        var catalog = Assert.Throws<ConfigurationException>(() => single.Catalog(id, null));
        Assert.Equal($"session \"{id}\" is closing (done)", catalog.Message);
        var full = await Assert.ThrowsAsync<ConfigurationException>(() => single.OpenAsync(new OpenSessionOptions()));
        Assert.Equal("SESSION_OPEN", full.Code);
        released.SetResult();
        Assert.Contains($"Session {id} closed (done)", await closing, StringComparison.Ordinal);
        Assert.Equal(await closing, await again);
        Assert.Contains("button \"Submit\"", await single.OpenAsync(new OpenSessionOptions()), StringComparison.Ordinal);
        await single.CloseAsync("done");
    }

    [Fact]
    public async Task Refuses_a_session_on_another_config_while_one_is_open()
    {
        var mixed = Host(new FakeEngine(), maxSessions: 2);
        var first = SessionId(await mixed.OpenAsync(new OpenSessionOptions()));
        var other = await Assert.ThrowsAsync<ConfigurationException>(() => mixed.OpenAsync(new OpenSessionOptions { Config = "other.config.json" }));
        Assert.Equal("CONFIG_IN_USE", other.Code);
        Assert.Equal(
            $"session {first} is open on config {Path.Combine(_dir, "e2e.config.json")}; sessions open at once share one config, because the secrets they redact are shared process-wide; open this one on that config, or close every session on it first",
            other.Message);
        await mixed.CloseAsync("done");
        Assert.Contains($"config {Path.Combine(_dir, "other.config.json")}", await mixed.OpenAsync(new OpenSessionOptions { Config = "other.config.json" }), StringComparison.Ordinal);
        await mixed.CloseAsync("done");
    }

    [Fact]
    public async Task Refuses_a_session_on_another_config_before_reading_it()
    {
        var read = new List<string>();
        var mixed = Host(new FakeEngine(), maxSessions: 2, onLoad: path => read.Add(Path.GetFileName(path)));
        await mixed.OpenAsync(new OpenSessionOptions());
        var other = await Assert.ThrowsAsync<ConfigurationException>(() => mixed.OpenAsync(new OpenSessionOptions { Config = "other.config.json" }));
        Assert.Equal("CONFIG_IN_USE", other.Code);
        Assert.Equal(["e2e.config.json"], read);
        await mixed.CloseAsync("done");
    }

    [Fact]
    public async Task Cancels_a_session_still_opening_when_it_shuts_down_waits_for_it_and_admits_none_meanwhile()
    {
        var loading = new TaskCompletionSource();
        var fake = new FakeEngine();
        var shutting = Host(fake, loaded: loading.Task);
        var opening = shutting.OpenAsync(new OpenSessionOptions());
        var shutDown = false;
        var closed = shutting.CloseAllAsync("server shutdown").ContinueWith(
            task =>
            {
                shutDown = true;
                return task.Result;
            },
            TaskScheduler.Default);
        var refused = await Assert.ThrowsAsync<ConfigurationException>(() => shutting.OpenAsync(new OpenSessionOptions()));
        Assert.Equal("SESSION_OPEN", refused.Code);
        Assert.Equal("the server is shutting down; no session can open", refused.Message);
        await Task.Delay(50);
        Assert.False(shutDown);
        loading.SetResult();
        var cancelled = await Assert.ThrowsAsync<E2EException>(() => opening);
        Assert.Equal("CANCELLED", cancelled.Code);
        Assert.Null(await closed);
        Assert.False(shutting.IsOpen);
        Assert.Equal(0, fake.AttemptsStarted);
    }

    [Fact]
    public async Task Tears_down_an_attempt_whose_first_observation_failed_then_opens_the_next_one()
    {
        var broken = true;
        var fake = new FakeEngine
        {
            OnObserve = () =>
            {
                if (broken)
                {
                    throw new InvalidOperationException("screen unavailable");
                }
            },
        };
        var flaky = Host(fake);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => flaky.OpenAsync(new OpenSessionOptions()));
        Assert.Contains("screen unavailable", failure.Message, StringComparison.Ordinal);
        Assert.False(flaky.IsOpen);
        Assert.Equal(1, fake.AttemptsStarted);
        Assert.Equal(1, fake.Disposes);
        broken = false;
        Assert.Contains("button \"Submit\"", await flaky.OpenAsync(new OpenSessionOptions()), StringComparison.Ordinal);
        await flaky.CloseAsync("done");
        Assert.Equal(2, fake.AttemptsStarted);
        Assert.Equal(2, fake.Disposes);
    }

    [Fact]
    public async Task Redacts_a_secret_an_engine_error_carries_from_the_cleanup_line_of_close_session_and_the_shutdown_summary()
    {
        var fake = new FakeEngine { OnDispose = () => throw new InvalidOperationException("end failed for admin:kiosk-pw") };
        var leaky = Host(fake, secrets: "\"adminPassword\": \"kiosk-pw\"");
        var specs = leaky.ToolSpecs().ToDictionary(spec => spec.Name);
        await specs["open_session"].Call(Json("{}"), CancellationToken.None);
        var closed = await specs["close_session"].Call(Json("{}"), CancellationToken.None);
        var text = Text(closed);
        Assert.Contains("Cleanup:", text, StringComparison.Ordinal);
        Assert.Contains("admin:<secret:adminPassword>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("kiosk-pw", text, StringComparison.Ordinal);
        await leaky.OpenAsync(new OpenSessionOptions());
        var summary = await leaky.CloseAllAsync("server shutdown");
        Assert.Contains("admin:<secret:adminPassword>", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("kiosk-pw", summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Redacts_the_configs_secrets_from_an_open_session_failure_and_the_log_before_any_session_exists()
    {
        var fake = new FakeEngine { OnStart = () => throw new InvalidOperationException("boot failed with token open-fail-token-1") };
        var leaky = Host(fake, secrets: "\"bootToken\": \"open-fail-token-1\"");
        var result = await leaky.ToolSpecs()[0].Call(Json("{}"), CancellationToken.None);
        var text = Text(result);
        Assert.True(result.IsError);
        Assert.Contains("<secret:bootToken>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("open-fail-token-1", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Catalogs_only_the_verbs_the_engine_declares()
    {
        var host = Host(new FakeEngine());
        var opened = await host.OpenAsync(new OpenSessionOptions());
        var names = ServedCli.CatalogNames(opened);
        Assert.Equal(["observe", "tap", "double_tap", "type", "press", "select", "check", "uncheck", "clear", "navigate", "locate"], names);
        var call = host.ToolSpecs().Single(spec => spec.Name == "call");
        var scrolled = await call.Call(Json("""{ "tool": "scroll", "args": { "direction": "down" } }"""), CancellationToken.None);
        Assert.True(scrolled.IsError);
        Assert.Contains("UNSUPPORTED_CAPABILITY", Text(scrolled), StringComparison.Ordinal);
        Assert.Contains("declares no such action", Text(scrolled), StringComparison.Ordinal);
        await host.CloseAsync("done");
    }

    private static JsonElement Json(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static string Text(ModelContextProtocol.Protocol.CallToolResult result) =>
        string.Join('\n', result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(part => part.Text));

    private static string SessionId(string text) => Regex.Match(text, @"^Session (\S+) open", RegexOptions.None, TimeSpan.FromSeconds(5)).Groups[1].Value;

    private static async Task UntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "the condition did not hold in time");
            await Task.Delay(20);
        }
    }

    /// <summary>A host whose configs take their engine from <paramref name="engine"/>, one config file at a time.</summary>
    private SessionHost Host(
        FakeEngine engine,
        List<bool>? headedValues = null,
        bool headed = false,
        TimeSpan? idle = null,
        TimeSpan? lifetime = null,
        int maxSessions = 4,
        Task? loaded = null,
        Action<string>? onLoad = null,
        string secrets = "")
    {
        var host = new SessionHost(new SessionHostOptions
        {
            LocateConfig = requested => Path.Combine(_dir, requested ?? "e2e.config.json"),
            LoadConfig = async path =>
            {
                if (loaded is not null)
                {
                    await loaded;
                }

                onLoad?.Invoke(path);
                return E2EConfig.Parse(FixtureProject.Config(Url, secrets), _dir);
            },
            CreateEngine = value =>
            {
                headedValues?.Add(value);
                return engine;
            },
            Headed = headed,
            MaxSessions = maxSessions,
            Idle = idle ?? SessionHost.SessionIdle,
            Ttl = lifetime ?? SessionHost.SessionTtl,
            Log = (level, message) =>
            {
                lock (_logs)
                {
                    _logs.Add(level + ": " + message);
                }
            },
        });
        _hosts.Add(host);
        return host;
    }
}
