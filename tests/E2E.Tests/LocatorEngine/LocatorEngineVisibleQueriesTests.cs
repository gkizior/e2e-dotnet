// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.LocatorEngine;

/// <summary>
/// The port resolves a query over the observed tree, so a node that reports no visibility is shown and a visible query
/// under an index is not handed to an engine. So the upstream its "keeps nodes whose engine reports no visibility at all"
/// and "hands a visible query under an index to the engine unchanged: only it can filter before nth" are not ported. And
/// a role query never matches a hidden node, so the getByRole row of "without visible keeps the hidden twin, so the pair
/// is LOCATOR_AMBIGUOUS" is not ported (COMPATIBILITY, Locators).
/// </summary>
public sealed class LocatorEngineVisibleQueriesTests
{
    /// <summary>A visible node and its hidden twin, as an engine that ignores <c>visible</c> would report them.</summary>
    private static readonly SemanticNode[] Twins =
    [
        new() { Ref = "shown", Role = "button", Name = "Save", Text = "Save", Placeholder = "Save", Value = "Save", TestId = "save" },
        new() { Ref = "hidden", Role = "button", Name = "Save", Text = "Save", Placeholder = "Save", Value = "Save", TestId = "save", States = new NodeStates { Hidden = true } },
    ];

    private static Screen ScreenOver(IReadOnlyList<SemanticNode> nodes, List<string> tapped) => new(
        _ => Task.FromResult(new Observation { Route = "/", Roots = nodes }),
        (node, _, _) =>
        {
            tapped.Add(node.Ref);
            return Task.CompletedTask;
        },
        () => CancellationToken.None,
        () => { },
        TimeSpan.FromMilliseconds(300),
        TimeSpan.FromMilliseconds(300));

    private static Locator Query(Screen screen, string kind, bool visible)
    {
        var options = new TextMatchOptions { Visible = visible };
        return kind switch
        {
            "getByRole" => screen.GetByRole("button", new RoleOptions { Name = "Save", Visible = visible }),
            "getByText" => screen.GetByText("Save", options),
            "getByLabel" => screen.GetByLabel("Save", options),
            "getByPlaceholder" => screen.GetByPlaceholder("Save", options),
            "getByDisplayValue" => screen.GetByDisplayValue("Save", options),
            "getByTestId" => screen.GetByTestId("save", options),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null),
        };
    }

    [Theory]
    [InlineData("getByRole")]
    [InlineData("getByText")]
    [InlineData("getByLabel")]
    [InlineData("getByPlaceholder")]
    [InlineData("getByDisplayValue")]
    [InlineData("getByTestId")]
    public async Task With_visible_true_resolves_the_shown_twin_and_ignores_the_hidden_one(string kind)
    {
        var tapped = new List<string>();
        var screen = ScreenOver(Twins, tapped);

        await Query(screen, kind, visible: true).TapAsync();

        Assert.Equal(["shown"], tapped);
        Assert.Equal(1, await Query(screen, kind, visible: true).CountAsync());
    }

    [Theory]
    [InlineData("getByText")]
    [InlineData("getByLabel")]
    [InlineData("getByPlaceholder")]
    [InlineData("getByDisplayValue")]
    [InlineData("getByTestId")]
    public async Task Without_visible_keeps_the_hidden_twin_so_the_pair_is_LOCATOR_AMBIGUOUS(string kind)
    {
        var screen = ScreenOver(Twins, []);

        var error = await Assert.ThrowsAsync<TestException>(() => Query(screen, kind, visible: false).TapAsync());

        Assert.Equal("STRICT_MODE", error.Code);
        Assert.Equal(2, await Query(screen, kind, visible: false).CountAsync());
    }

    [Fact]
    public async Task Names_the_predicate_in_the_ambiguity_message_when_two_visible_nodes_remain()
    {
        var screen = ScreenOver(
            [new SemanticNode { Ref = "a", Role = "button", Name = "Save", Text = "Save" }, new SemanticNode { Ref = "b", Role = "button", Name = "Save", Text = "Save" }],
            []);

        var error = await Assert.ThrowsAsync<TestException>(() => screen.GetByText("Save", new TextMatchOptions { Visible = true }).TapAsync());

        Assert.Equal("STRICT_MODE", error.Code);
        Assert.Contains("getByText(\"Save\", visible: true)", error.Message, StringComparison.Ordinal);
    }
}
