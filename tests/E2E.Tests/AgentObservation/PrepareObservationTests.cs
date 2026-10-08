// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Internal;

namespace E2E.Tests.AgentObservation;

/// <summary>
/// The port renders the screen as one text block in its own format, with no pixel evidence, executor tree, node
/// index, byte budget, cut fields, or route path. So these upstream its are not ported: "keeps unavailable semantics
/// separate from an empty tree and requires permitted masked evidence", "keeps the whole address after the origin as
/// the path, fragment included, for the route check to read", "returns evidence without a comparable shape without
/// repeating capture", "withholds pixels whose masking the engine cannot prove and keeps the tree", "redacts the leading
/// part of a secret a field cut at its limit ends with, and leaves a cut plain value and a whole field alone", "redacts
/// every string field a node carries, in the text, the executor tree, and the node index alike", "collapses a field
/// whose collapsed form shows a secret its written form hides, so no later reader brings it back", "redacts an earlier
/// capture again with a secret resolved since, keeping which nodes are leaves", "truncates at the byte limit while
/// keeping the root and flagging truncation", and "reports the byte cut, not the engine cut, when both apply".
/// </summary>
public sealed class PrepareObservationTests
{
    /// <summary>The port renders the screen in its own format and marker, and counts no bytes or nodes beside the text.</summary>
    [Fact]
    public void Carries_an_engine_reported_cut_through_as_truncated_with_its_own_marker()
    {
        var tree = new SemanticNode { Ref = "n1", Role = "document", Children = [new SemanticNode { Ref = "n2", Role = "button", Name = "One" }] };
        var text = SnapshotText.Render(new Observation { Route = "/", Roots = [tree], Truncated = true }, Redactor.None);
        Assert.Contains("button \"One\"", text, StringComparison.Ordinal);
        Assert.EndsWith("\n(More of the page is off screen. Scroll to reach it.)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Serializes_the_tree_with_node_references_and_indentation()
    {
        var tree = new SemanticNode
        {
            Ref = "n1",
            Role = "document",
            Name = "Home",
            Children =
            [
                new SemanticNode { Ref = "n2", Role = "heading", Name = "Welcome" },
                new SemanticNode { Ref = "n3", Role = "button", Name = "Buy", States = new NodeStates { Disabled = true } },
            ],
        };
        var text = SnapshotText.Render(new Observation { Route = "/", Roots = [tree] }, Redactor.None);
        Assert.Equal(
            ["Screen (/):", "- document \"Home\" [ref=n1]", "  - heading \"Welcome\" [ref=n2]", "  - button \"Buy\" [ref=n3] [disabled]"],
            text.Split(Environment.NewLine));
    }

    [Fact]
    public void Masks_secure_fields_and_never_renders_their_value()
    {
        var tree = new SemanticNode
        {
            Ref = "n1",
            Children = [new SemanticNode { Ref = "n2", Role = "textbox", Name = "Password", Value = "should-not-appear", InputPurpose = "password", States = new NodeStates { Secure = true } }],
        };
        var text = SnapshotText.Render(new Observation { Route = "/", Roots = [tree] }, Redactor.None);
        Assert.Contains(" secure", text, StringComparison.Ordinal);
        Assert.Contains("purpose=password", text, StringComparison.Ordinal);
        Assert.DoesNotContain("should-not-appear", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Replaces_every_exact_registered_secret_value_with_its_stable_name()
    {
        var tree = new SemanticNode
        {
            Ref = "n1",
            Children =
            [
                new SemanticNode { Ref = "n2", Role = "status", Text = "signed in as hunter2 (hunter2)" },
                new SemanticNode { Ref = "n3", Role = "textbox", Name = "hunter2", Value = "hunter2" },
            ],
        };
        var text = SnapshotText.Render(new Observation { Route = "/", Roots = [tree] }, Redactor.For([Secret.Create("member", "hunter2")]));
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.Contains("<secret:member>", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Collapses_whitespace_and_strips_control_characters_from_app_text()
    {
        var text = SnapshotText.Render(new Observation { Route = "/", Roots = [new SemanticNode { Ref = "n1", Role = "status", Text = "line\u0007one\n   two  " }] }, Redactor.None);
        Assert.Contains("\"line\uFFFDone two\"", text, StringComparison.Ordinal);
    }
}
