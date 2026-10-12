// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Cli.Mcp;
using E2E.Engine;
using E2E.Internal;

namespace E2E.Cli.Tests.McpSession;

/// <summary><c>locate</c> builds the query and the test code a coding agent writes.</summary>
public sealed class LocateQueryTests
{
    [Fact]
    public void Builds_each_query_kind_and_the_matching_screen_call_carrying_exact_false_into_both()
    {
        var query = SessionCatalog.LocateQuery(new LocateArgs { Role = "button", Name = "Save" });
        Assert.Equal("Screen.GetByRole(\"button\", \"Save\")", query.Code);
        Assert.Equal("role", query.Query.Kind);
        Assert.True(query.Query.Exact);
        var text = SessionCatalog.LocateQuery(new LocateArgs { Text = "sign", Exact = false });
        Assert.Equal("Screen.GetByText(\"sign\", exact: false)", text.Code);
        Assert.Equal("text", text.Query.Kind);
        Assert.False(text.Query.Exact);
        Assert.Equal("Screen.GetByLabel(\"Email\")", SessionCatalog.LocateQuery(new LocateArgs { Label = "Email" }).Code);
        Assert.Equal("Screen.GetByPlaceholder(\"you@example.test\")", SessionCatalog.LocateQuery(new LocateArgs { Placeholder = "you@example.test" }).Code);
        Assert.Equal("Screen.GetByTestId(\"items\")", SessionCatalog.LocateQuery(new LocateArgs { TestId = "items" }).Code);
    }

    [Fact]
    public void Rejects_zero_or_several_query_kinds_and_a_name_without_a_role()
    {
        Assert.Matches("exactly one of role, text, label, placeholder, or testId", Assert.Throws<ConfigurationException>(() => SessionCatalog.LocateQuery(new LocateArgs())).Message);
        Assert.Matches("exactly one", Assert.Throws<ConfigurationException>(() => SessionCatalog.LocateQuery(new LocateArgs { Role = "button", Text = "Save" })).Message);
        Assert.Matches("name only narrows a role query", Assert.Throws<ConfigurationException>(() => SessionCatalog.LocateQuery(new LocateArgs { Text = "Save", Name = "Save" })).Message);
    }
}
