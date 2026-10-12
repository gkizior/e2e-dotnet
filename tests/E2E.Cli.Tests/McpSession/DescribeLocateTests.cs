// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Cli.Mcp;
using E2E.Engine;
using E2E.Internal;

namespace E2E.Cli.Tests.McpSession;

/// <summary><c>locate</c> tells the agent which outcome a test using the locator would have, and shows no secret.</summary>
public sealed class DescribeLocateTests
{
    private static SemanticNode Node(string role, string name, string? value = null, string? text = null, NodeStates? states = null) =>
        new() { Ref = "l1", Role = role, Name = name, Value = value, Text = text, States = states ?? new NodeStates() };

    [Fact]
    public void Tells_the_agent_which_test_outcome_the_locator_would_have()
    {
        var query = SessionCatalog.LocateQuery(new LocateArgs { Role = "button", Name = "Save" });
        Assert.Equal(
            "1 node matches getByRole(\"button\", \"Save\").\nUse: Screen.GetByRole(\"button\", \"Save\")\n- button \"Save\"",
            SessionCatalog.DescribeLocate(query, 1, [Node("button", "Save")], Redactor.None));
        Assert.Contains("NOT_FOUND", SessionCatalog.DescribeLocate(query, 0, [], Redactor.None), StringComparison.Ordinal);
        var ambiguous = SessionCatalog.DescribeLocate(query, 12, [Node("button", "Save", states: new NodeStates { Disabled = true })], Redactor.None);
        Assert.Contains("12 nodes match", ambiguous, StringComparison.Ordinal);
        Assert.Contains("STRICT_MODE", ambiguous, StringComparison.Ordinal);
        Assert.Contains("- button \"Save\" [disabled]", ambiguous, StringComparison.Ordinal);
        Assert.Contains("- and 11 more", ambiguous, StringComparison.Ordinal);
    }

    [Fact]
    public void Never_shows_the_value_of_a_secure_node()
    {
        var query = SessionCatalog.LocateQuery(new LocateArgs { Label = "Password" });
        var text = SessionCatalog.DescribeLocate(query, 1, [Node("textbox", "Password", value: "hunter2", states: new NodeStates { Secure = true })], Redactor.None);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Renders_a_registered_secret_in_a_plain_node_by_its_name_never_the_plaintext()
    {
        var redactor = Redactor.For([Secret.Create("apiKey", "sk-live-SUPERSECRET-0000")]);
        var query = SessionCatalog.LocateQuery(new LocateArgs { Label = "API key" });
        var filled = Node("textbox", "API key", value: "sk-live-SUPERSECRET-0000");
        var echoed = Node("status", "Saved key", text: "Saved sk-live-SUPERSECRET-0000");
        var text = SessionCatalog.DescribeLocate(query, 2, [filled, echoed], redactor);
        Assert.DoesNotContain("sk-live-SUPERSECRET-0000", text, StringComparison.Ordinal);
        Assert.Contains("- textbox \"API key\" value \"<secret:apiKey>\"", text, StringComparison.Ordinal);
        Assert.Contains("- status \"Saved key\" text \"Saved <secret:apiKey>\"", text, StringComparison.Ordinal);
    }
}
