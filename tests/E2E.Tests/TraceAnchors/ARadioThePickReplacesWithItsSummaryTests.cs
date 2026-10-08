// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.TraceAnchors.AnchorFixtures;

namespace E2E.Tests.TraceAnchors;

/// <summary><c>describeDelta</c>, a radio the pick replaces with its summary.</summary>
public sealed class ARadioThePickReplacesWithItsSummaryTests
{
    private static readonly SemanticNode[] Picker = [Node("radio", "Standard"), Node(text: "Standard"), Node("radio", "Express"), Node(text: "Express")];
    private static readonly SemanticNode[] Start = [Heading, .. Picker, DeliveryState("unset")];
    private static readonly SemanticNode[] Summary = [Node(text: "Express delivery selected"), Node("button", "Change delivery")];
    private static readonly SemanticNode[] End = [Heading, .. Summary, DeliveryState("Express")];
    private static readonly (List<RecordedTarget> Appeared, List<RecordedTarget> Gone) Delta = Describe(Start, End);
    private static readonly CacheEntry Recorded = new() { Appeared = Delta.Appeared, Gone = Delta.Gone };

    [Fact]
    public void Holds_on_the_screen_it_was_recorded_on_so_a_faithful_replay_passes()
    {
        Assert.True(Anchors.Holds(Recorded, Project(End), Project(Start)));
    }

    [Fact]
    public void Records_no_vanished_label_that_a_node_left_on_screen_still_reads_as()
    {
        // The status reads "Express" too: the label is gone, but an anchor of its text alone is not.
        Assert.DoesNotContain("|~Express", Shapes(Delta.Gone));
        Assert.Contains("radio|Express", Shapes(Delta.Gone));
        Assert.Contains("|~Standard", Shapes(Delta.Gone));
    }

    [Fact]
    public void Still_fails_a_replay_whose_pick_left_the_radios_on_screen()
    {
        Assert.False(Anchors.Holds(Recorded, Project(Start), Project(Start)));
        Assert.False(Anchors.Holds(Recorded, Project([Heading, .. Picker, .. Summary, DeliveryState("Express")]), Project(Start)));
    }

    private static SemanticNode DeliveryState(string text) => Node("status", "Delivery state", text: text);
}
