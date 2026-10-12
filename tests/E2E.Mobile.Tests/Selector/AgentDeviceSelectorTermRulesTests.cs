// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using static E2E.Mobile.Tests.RawNodes;

namespace E2E.Mobile.Tests.Selector;

public sealed class AgentDeviceSelectorTermRulesTests
{
    private static readonly Rect Box = new(0, 100, 200, 40);

    /// <summary>An Android form whose nodes fall on each side of agent-device's term rules.</summary>
    private static readonly IReadOnlyList<ProjectedNode> AndroidForm = Project(
        N("e1", index: 0, depth: 0, type: "android.widget.FrameLayout", rect: R(0, 0, 400, 800)),
        N("e2", index: 1, parentIndex: 0, depth: 1, type: "android.widget.EditText", identifier: "email", enabled: true, rect: Box),
        N("e3", index: 2, parentIndex: 0, depth: 1, type: "android.widget.EditText", identifier: "locked", enabled: false, rect: Box),
        N("e4", index: 3, parentIndex: 0, depth: 1, type: "android.widget.Button", identifier: "Submit", label: "Submit", hittable: true, rect: Box),
        N("e5", index: 4, parentIndex: 0, depth: 1, type: "android.view.View", identifier: "spacer", rect: R(0, 0, 0, 0))).Index;

    private static List<string?> Ids(string selector) =>
        [.. AgentDeviceSelector.Compile(selector, MobilePlatform.Android)(AndroidForm).Select(entry => entry.Raw.Identifier)];

    [Fact]
    public void Reads_editable_as_a_fillable_type_that_is_enabled()
    {
        Assert.Equal(["email"], Ids("editable"));
        Assert.DoesNotContain("email", Ids("editable=false"));
    }

    [Fact]
    public void Compares_text_case_insensitively()
    {
        Assert.Equal(["email"], Ids("id=EMAIL"));
        Assert.Equal(["Submit"], Ids("label=submit"));
    }

    [Fact]
    public void Reads_visible_as_hittable_or_a_non_empty_frame()
    {
        Assert.Equal(["spacer"], Ids("hidden"));
        Assert.Equal([null, "email", "locked", "Submit"], Ids("visible"));
    }

    [Fact]
    public void Matches_hittable_only_on_a_node_that_reports_it()
    {
        Assert.Equal(["Submit"], Ids("hittable"));
    }

    [Fact]
    public void Matches_every_term_of_an_alternative_and_enabled_by_the_node_flag()
    {
        Assert.Equal(["locked"], Ids("enabled=false"));
        Assert.Equal(["email"], Ids("role=text-field enabled=true"));
    }
}
