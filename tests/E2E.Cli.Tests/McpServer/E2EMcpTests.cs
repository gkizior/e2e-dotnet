// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using ModelContextProtocol.Client;
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
        var client = _served.Client;
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

    /// <summary>One <c>e2e mcp</c> process and its client, shared by the tests, as upstream's <c>beforeAll</c> starts it.</summary>
    public sealed class Served : IAsyncLifetime
    {
        private readonly string _dir = Directory.CreateTempSubdirectory("e2e-mcp-").FullName;

        public McpClient Client { get; private set; } = null!;

        /// <summary>A fixed tool's result: the text parts joined, and whether it is an error.</summary>
        public async Task<(string Text, bool IsError)> InvokeAsync(string name, Dictionary<string, object?>? args = null)
        {
            var result = await Client.CallToolAsync(name, args ?? []);
            return (string.Join('\n', result.Content.OfType<TextContentBlock>().Select(part => part.Text)), result.IsError == true);
        }

        public async Task InitializeAsync()
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "e2e",
                Command = "dotnet",
                Arguments = [Path.Combine(AppContext.BaseDirectory, "E2E.Cli.dll"), "mcp"],
                WorkingDirectory = _dir,
                EnvironmentVariables = new Dictionary<string, string?> { ["CI"] = "" },
            });
            Client = await McpClient.CreateAsync(transport, new McpClientOptions { ClientInfo = new Implementation { Name = "e2e-mcp-test", Version = "0.0.0" } });
        }

        public async Task DisposeAsync()
        {
            await Client.DisposeAsync();
            Directory.Delete(_dir, recursive: true);
        }
    }
}

