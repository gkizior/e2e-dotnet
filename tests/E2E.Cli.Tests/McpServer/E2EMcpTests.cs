// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Cli.Tests.Helpers;
using ModelContextProtocol.Protocol;

namespace E2E.Cli.Tests.McpServer;

/// <summary>
/// <c>e2e mcp</c> end to end: the built CLI serving over stdio to a real MCP client. The server's tool list is
/// four tools; everything a session can do is a catalog behind <c>call</c>.
/// </summary>
public sealed class E2EMcpTests : IClassFixture<E2EMcpTests.Served>
{
    private readonly Served _served;

    public E2EMcpTests(Served served)
    {
        _served = served;
    }

    [Fact]
    public async Task Serves_four_fixed_tools_and_the_guide_resources()
    {
        var client = _served.Cli.Client;
        var tools = await client.ListToolsAsync();
        Assert.Equal(["open_session", "tools", "call", "close_session"], tools.Select(tool => tool.Name));
        var callTool = tools.Single(tool => tool.Name == "call").ProtocolTool;
        Assert.Contains("call {tool: \"tap\", args: {target: \"n42\"}, session: \"<id>\"}", callTool.Description, StringComparison.Ordinal);
        Assert.Equal("object", callTool.InputSchema.GetProperty("type").GetString());
        Assert.Equal(["tool"], callTool.InputSchema.GetProperty("required").EnumerateArray().Select(name => name.GetString()));
        Assert.True(tools.Single(tool => tool.Name == "tools").ProtocolTool.Annotations?.ReadOnlyHint);
        var open = tools.Single(tool => tool.Name == "open_session").ProtocolTool.InputSchema.GetProperty("properties");
        Assert.True(open.TryGetProperty("target", out _));
        Assert.True(open.TryGetProperty("config", out _));
        Assert.Equal("boolean", open.GetProperty("headed").GetProperty("type").GetString());
        Assert.Contains("call {tool, args} runs any catalog tool", client.ServerInstructions, StringComparison.Ordinal);
        Assert.Contains("Several sessions can be open at once", client.ServerInstructions, StringComparison.Ordinal);

        var resources = await client.ListResourcesAsync();
        Assert.Superset(new HashSet<string>(["e2e://guide", "e2e://guide/mcp", "e2e://guide/writing-tests"]), resources.Select(resource => resource.Uri).ToHashSet());
        var guide = await client.ReadResourceAsync("e2e://guide/mcp");
        Assert.Contains("# Driving the app over MCP", Assert.IsType<TextResourceContents>(guide.Contents[0]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_tools_and_call_before_a_session_is_open()
    {
        var listed = await _served.InvokeAsync("tools");
        Assert.True(listed.IsError);
        Assert.Contains("NO_SESSION", listed.Text, StringComparison.Ordinal);
        var observed = await _served.InvokeAsync("call", new() { ["tool"] = "observe" });
        Assert.True(observed.IsError);
        Assert.Contains("NO_SESSION", observed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refuses_an_argument_a_fixed_tool_does_not_declare_at_the_protocol_layer_before_anything_runs()
    {
        // The server validates against the closed schema before the handler runs: the refusal names the tool
        // and the key, and no session opened.
        foreach (var (name, args, key) in new (string, Dictionary<string, object?>, string)[]
        {
            ("open_session", new() { ["headless"] = true }, "headless"),
            ("tools", new() { ["all"] = true }, "all"),
            ("call", new() { ["tool"] = "observe", ["verbose"] = true }, "verbose"),
            ("close_session", new() { ["force"] = true }, "force"),
        })
        {
            var refused = await _served.InvokeAsync(name, args);
            Assert.True(refused.IsError, name);
            Assert.Contains("Invalid arguments for tool " + name + ": Unrecognized key: \"" + key + "\"", refused.Text, StringComparison.Ordinal);
        }

        var listed = await _served.InvokeAsync("tools");
        Assert.True(listed.IsError);
        Assert.Contains("NO_SESSION", listed.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opens_a_session_then_locates_acts_fills_a_secret_and_withholds_screenshots_afterwards()
    {
        var opened = await _served.InvokeAsync("open_session");
        Assert.False(opened.IsError, opened.Text);
        Assert.Matches(new Regex(@"^Session \S+ open on target ""web"" \(platform web, engine web [^)]+\), headless; config .*e2e\.config\.json\.", RegexOptions.None, TimeSpan.FromSeconds(5)), opened.Text);
        Assert.Contains($"App: {_served.App.Url}/.", opened.Text, StringComparison.Ordinal);
        Assert.Contains("Secrets: \"adminPassword\", \"apiKey\".", opened.Text, StringComparison.Ordinal);
        Assert.Contains("Tools (run one with call {tool, args}; tools {tool} shows a tool's arguments):", opened.Text, StringComparison.Ordinal);
        string[] names =
        [
            "observe", "tap", "double_tap", "type", "press", "select", "check", "uncheck", "clear", "scroll_to", "scroll", "navigate", "back", "screenshot", "type_secret", "locate",
        ];
        Assert.Equal(names, ServedCli.CatalogNames(opened.Text));
        Assert.Contains("- tap {target}: Tap or click one node: the gesture for a button, link, menu item, tab, checkbox, row, or field.\n", opened.Text, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"^- locate \{role\?, name\?, text\?, label\?, placeholder\?, testId\?, exact\?\}: .* \[read-only\]$", RegexOptions.Multiline, TimeSpan.FromSeconds(5)), opened.Text);
        Assert.Contains("Screen (/):", opened.Text, StringComparison.Ordinal);
        Assert.Contains("button \"Increment\"", opened.Text, StringComparison.Ordinal);
        var sessionId = Regex.Match(opened.Text, @"^Session (\S+) open", RegexOptions.None, TimeSpan.FromSeconds(5)).Groups[1].Value;

        var listed = await _served.InvokeAsync("tools");
        Assert.False(listed.IsError, listed.Text);
        Assert.Contains($"Session {sessionId} on target \"web\": 16 tools.", listed.Text, StringComparison.Ordinal);
        Assert.Equal(ServedCli.CatalogLines(opened.Text), ServedCli.CatalogLines(listed.Text));
        var detail = await _served.InvokeAsync("tools", new() { ["tool"] = "type_secret" });
        Assert.Contains("Arguments (JSON Schema):", detail.Text, StringComparison.Ordinal);
        Assert.Contains("\"required\"", detail.Text, StringComparison.Ordinal);
        var unknown = await _served.InvokeAsync("tools", new() { ["tool"] = "teleport" });
        Assert.True(unknown.IsError);
        Assert.Contains("UNKNOWN_TOOL", unknown.Text, StringComparison.Ordinal);
        Assert.Contains("tools: observe, tap, double_tap, type, press", unknown.Text, StringComparison.Ordinal);

        var unique = await _served.CallAsync("locate", new() { ["role"] = "button", ["name"] = "Increment" });
        Assert.False(unique.IsError, unique.Text);
        Assert.Contains("1 node matches", unique.Text, StringComparison.Ordinal);
        Assert.Contains("Use: Screen.GetByRole(\"button\", \"Increment\")", unique.Text, StringComparison.Ordinal);
        var ambiguous = await _served.CallAsync("locate", new() { ["text"] = "Duplicated" });
        Assert.Contains("2 nodes match", ambiguous.Text, StringComparison.Ordinal);
        Assert.Contains("STRICT_MODE", ambiguous.Text, StringComparison.Ordinal);
        var missing = await _served.CallAsync("locate", new() { ["label"] = "Nowhere" });
        Assert.Contains("0 nodes match", missing.Text, StringComparison.Ordinal);
        Assert.Contains("NOT_FOUND", missing.Text, StringComparison.Ordinal);

        var observed = await _served.CallAsync("observe");
        var tapped = await _served.CallAsync("tap", new() { ["target"] = ServedCli.RefOf(observed.Text, "button \"Increment\"") });
        Assert.False(tapped.IsError, tapped.Text);
        Assert.StartsWith("tapped ", tapped.Text, StringComparison.Ordinal);
        var after = await _served.CallAsync("observe");
        Assert.Contains("status \"Counter\" text=\"1\"", after.Text, StringComparison.Ordinal);

        // Arguments are checked against the tool's own schema before it runs.
        var malformed = await _served.CallAsync("tap", new() { ["target"] = 42 });
        Assert.True(malformed.IsError);
        Assert.StartsWith("INVALID_ARGUMENT: call tap: target: ", malformed.Text, StringComparison.Ordinal);
        Assert.Contains("tools {tool: \"tap\"} shows its arguments", malformed.Text, StringComparison.Ordinal);

        // An argument the tool does not declare is refused the same way, never stripped and acted on.
        var decorated = await _served.CallAsync("tap", new() { ["target"] = ServedCli.RefOf(after.Text, "button \"Increment\""), ["force"] = true });
        Assert.True(decorated.IsError);
        Assert.Equal("INVALID_ARGUMENT: call tap: Unrecognized key: \"force\"; tools {tool: \"tap\"} shows its arguments", decorated.Text);
        var nameless = await _served.CallAsync("teleport");
        Assert.True(nameless.IsError);
        Assert.Contains("UNKNOWN_TOOL", nameless.Text, StringComparison.Ordinal);
        var elsewhere = await _served.InvokeAsync("call", new() { ["tool"] = "observe", ["session"] = "not-this-one" });
        Assert.True(elsewhere.IsError);
        Assert.Contains($"NO_SESSION: session \"not-this-one\" is not open; open: {sessionId} on \"web\"", elsewhere.Text, StringComparison.Ordinal);

        // A stale ref is refused with its code first and the node named, and no other node is acted on.
        var stale = await _served.CallAsync("tap", new() { ["target"] = "n9999" });
        Assert.True(stale.IsError);
        Assert.StartsWith("tap n9999 failed: NOT_FOUND: node n9999 is not on the current screen", stale.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("tapped", stale.Text, StringComparison.Ordinal);

        var before = await _served.CallAsync("screenshot");
        Assert.False(before.IsError, before.Text);
        Assert.Equal(1, before.Images);
        Assert.Matches(new Regex(@"^Screenshot attached: \d+ by \d+ pixels \([\d.]+ per CSS pixel\)\.$", RegexOptions.None, TimeSpan.FromSeconds(5)), before.Text);

        var filled = await _served.CallAsync("type_secret", new() { ["target"] = ServedCli.RefOf(after.Text, "textbox \"Password\""), ["secret"] = "adminPassword" });
        Assert.False(filled.IsError, filled.Text);
        Assert.Contains("filled secret <secret:adminPassword>", filled.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(Served.AdminPassword, filled.Text, StringComparison.Ordinal);

        var tainted = await _served.CallAsync("screenshot");
        Assert.Equal(0, tainted.Images);
        Assert.Contains("No screenshot: a secret was filled in this attempt", tainted.Text, StringComparison.Ordinal);
        Assert.Contains("PIXEL_TAINTED", tainted.Text, StringComparison.Ordinal);

        foreach (var url in new[] { "javascript:alert(1)", "file:///etc/passwd", "view-source:file:///etc/passwd", "VIEW-SOURCE:http://example.test/", "about:srcdoc" })
        {
            var denied = await _served.CallAsync("navigate", new() { ["url"] = url });
            Assert.True(denied.IsError, url);
            Assert.Equal($"navigate {url} failed: POLICY_DENIED: forbidden URL scheme: {url[..(url.IndexOf(':', StringComparison.Ordinal) + 1)].ToLowerInvariant()}", denied.Text.Split('\n')[0]);
            Assert.DoesNotContain("navigated", denied.Text, StringComparison.Ordinal);
        }

        // A second session opens beside the first on its own browser: while both are open a call names its session.
        var second = await _served.InvokeAsync("open_session");
        Assert.False(second.IsError, second.Text);
        var secondId = Regex.Match(second.Text, @"^Session (\S+) open", RegexOptions.None, TimeSpan.FromSeconds(5)).Groups[1].Value;
        Assert.NotEqual(sessionId, secondId);
        Assert.Contains($"Pass session \"{secondId}\" to every tools, call, and close_session; with several sessions open, a call without it fails.", second.Text, StringComparison.Ordinal);
        var unnamed = await _served.CallAsync("observe");
        Assert.True(unnamed.IsError);
        Assert.StartsWith($"SESSION_REQUIRED: 2 sessions are open; pass session to name one: {sessionId} on \"web\", {secondId} on \"web\"", unnamed.Text, StringComparison.Ordinal);
        var secondScreen = await _served.InvokeAsync("call", new() { ["tool"] = "observe", ["session"] = secondId });
        Assert.Contains("status \"Counter\" text=\"0\"", secondScreen.Text, StringComparison.Ordinal);
        var firstScreen = await _served.InvokeAsync("call", new() { ["tool"] = "observe", ["session"] = sessionId });
        Assert.Contains("status \"Counter\" text=\"1\"", firstScreen.Text, StringComparison.Ordinal);
        var unnamedClose = await _served.InvokeAsync("close_session");
        Assert.Contains("SESSION_REQUIRED", unnamedClose.Text, StringComparison.Ordinal);
        var closedSecond = await _served.InvokeAsync("close_session", new() { ["session"] = secondId });
        Assert.False(closedSecond.IsError, closedSecond.Text);
        var endedCall = await _served.InvokeAsync("call", new() { ["tool"] = "observe", ["session"] = secondId });
        Assert.True(endedCall.IsError);
        Assert.Contains($"NO_SESSION: session \"{secondId}\" ended: closed by the agent; call open_session for a new one", endedCall.Text, StringComparison.Ordinal);

        var closed = await _served.InvokeAsync("close_session", new() { ["session"] = sessionId });
        Assert.False(closed.IsError, closed.Text);
        Assert.Matches(new Regex(@"^Session \S+ closed \(closed by the agent\); \d+ tool calls ran\.", RegexOptions.None, TimeSpan.FromSeconds(5)), closed.Text);
        Assert.DoesNotContain("Cleanup:", closed.Text, StringComparison.Ordinal);

        var gone = await _served.CallAsync("observe");
        Assert.True(gone.IsError);
        Assert.Contains("the previous session ended: closed by the agent", gone.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Opens_a_second_session_after_the_first_closed_on_an_explicit_config_path_and_names_an_unknown_target()
    {
        var unknown = await _served.InvokeAsync("open_session", new() { ["target"] = "nope" });
        Assert.True(unknown.IsError);
        Assert.Contains("UNKNOWN_TARGET", unknown.Text, StringComparison.Ordinal);

        var absent = await _served.InvokeAsync("open_session", new() { ["config"] = "missing.config.json" });
        Assert.True(absent.IsError);
        Assert.Contains("CONFIG_NOT_FOUND", absent.Text, StringComparison.Ordinal);

        var reopened = await _served.InvokeAsync("open_session", new() { ["target"] = "web", ["config"] = "e2e.config.json" });
        Assert.False(reopened.IsError, reopened.Text);
        Assert.Contains("button \"Increment\"", reopened.Text, StringComparison.Ordinal);
        var closed = await _served.InvokeAsync("close_session");
        Assert.False(closed.IsError, closed.Text);
    }

    [Fact]
    public async Task Shows_a_configured_secret_typed_into_a_plain_textbox_by_name_in_locate_and_observe_never_the_plaintext()
    {
        var opened = await _served.InvokeAsync("open_session");
        Assert.False(opened.IsError, opened.Text);
        Assert.Contains("Secrets: \"adminPassword\", \"apiKey\"", opened.Text, StringComparison.Ordinal);

        // The value goes in as text, not through type_secret: every output still shows a configured value by its name.
        var observed = await _served.CallAsync("observe");
        var filled = await _served.CallAsync("type", new() { ["target"] = ServedCli.RefOf(observed.Text, "textbox \"Focus target\""), ["text"] = Served.ApiKey });
        Assert.False(filled.IsError, filled.Text);

        var located = await _served.CallAsync("locate", new() { ["label"] = "Focus target" });
        Assert.False(located.IsError, located.Text);
        Assert.Contains("1 node matches", located.Text, StringComparison.Ordinal);
        Assert.True(located.Text.Contains("- textbox \"Focus target\" value \"<secret:apiKey>\"", StringComparison.Ordinal), located.Text);
        var after = await _served.CallAsync("observe");
        Assert.Contains("textbox \"Focus target\" [ref=", after.Text, StringComparison.Ordinal);
        Assert.Contains("value=\"<secret:apiKey>\"", after.Text, StringComparison.Ordinal);
        foreach (var output in new[] { filled, located, after })
        {
            Assert.DoesNotContain(Served.ApiKey, output.Text, StringComparison.Ordinal);
        }

        var closed = await _served.InvokeAsync("close_session");
        Assert.False(closed.IsError, closed.Text);
    }

    [Fact]
    public async Task Opens_sessions_headed_with_headed_and_headless_with_the_retired_headless()
    {
        foreach (var (flag, mode) in new[] { ("--headed", "headed"), ("--headless", "headless") })
        {
            // A headed browser needs a display; the host tests cover the headed flag where there is none.
            if (flag == "--headed" && OperatingSystem.IsLinux() && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
            {
                continue;
            }

            await using var other = await ServedCli.StartAsync(_served.Project.Directory, flag);
            var opened = await other.InvokeAsync("open_session");
            Assert.Contains($"), {mode};", opened.Text, StringComparison.Ordinal);
            await other.InvokeAsync("close_session");
        }
    }

    [Fact]
    public async Task Kept_stdout_for_the_protocol_the_servers_diagnostics_landed_on_stderr()
    {
        await _served.InvokeAsync("tools");
        Assert.Contains("e2e mcp: [info] e2e mcp", _served.Cli.Stderr, StringComparison.Ordinal);
    }

    /// <summary>One <c>e2e mcp</c> process, its project, and the fixture app, shared by the tests, as upstream's <c>beforeAll</c> starts them.</summary>
    public sealed class Served : IAsyncLifetime
    {
        internal const string AdminPassword = "admin-pass-4711";

        internal const string ApiKey = "sk-live-SUPERSECRET-0000";

        internal FixtureApp App { get; private set; } = null!;

        internal FixtureProject Project { get; private set; } = null!;

        internal ServedCli Cli { get; private set; } = null!;

        /// <summary>A fixed tool's result.</summary>
        internal Task<ToolText> InvokeAsync(string name, Dictionary<string, object?>? args = null) => Cli.InvokeAsync(name, args);

        /// <summary>A catalog tool through the server's <c>call</c> tool.</summary>
        internal Task<ToolText> CallAsync(string tool, Dictionary<string, object?>? args = null) => Cli.CallAsync(tool, args);

        public async Task InitializeAsync()
        {
            App = FixtureApp.Start();
            Project = new FixtureProject(new Dictionary<string, string>
            {
                ["e2e.config.json"] = FixtureProject.Config(App.Url, $"\"adminPassword\": \"{AdminPassword}\", \"apiKey\": \"{ApiKey}\""),
            });
            Cli = await ServedCli.StartAsync(Project.Directory);
        }

        public async Task DisposeAsync()
        {
            await Cli.DisposeAsync();
            Project.Dispose();
            App.Dispose();
        }
    }
}
