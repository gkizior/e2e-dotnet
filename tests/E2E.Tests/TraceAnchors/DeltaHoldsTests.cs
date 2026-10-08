// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.TraceAnchors.AnchorFixtures;

namespace E2E.Tests.TraceAnchors;

/// <summary><c>deltaHolds</c>: whether the recorded end state holds on a fresh screen.</summary>
public sealed class DeltaHoldsTests
{
    [Fact]
    public void Requires_every_recorded_field_text_included_unlike_target_relocation()
    {
        // Same named node, different text: the effect is missing, so the anchor is.
        var saved = Anchor("status", "Marker", text: "saved");

        Assert.False(Holds([saved], [Heading, EmptyMarker]));
        Assert.True(Holds([saved], [Heading, SavedMarker]));
    }

    [Fact]
    public void Is_presence_not_uniqueness()
    {
        Assert.True(Holds([Anchor("status", "Marker", text: "saved")], [SavedMarker, Node("status", "Marker", text: "saved")]));
    }

    [Fact]
    public void Requires_every_anchor()
    {
        var saved = Anchor("status", "Marker", text: "saved");
        var row = Anchor("link", "PB-Twin-Alpha");

        Assert.True(Holds([saved, row], [SavedMarker, Row]));
        Assert.False(Holds([saved, row], [SavedMarker]));
    }

    [Fact]
    public void Requires_every_vanished_node_to_be_gone_again()
    {
        var itemA = Node("listitem", "Item A");

        Assert.True(Holds([], [Heading], [Heading, itemA], [Anchor("listitem", "Item A")]));
        Assert.False(Holds([], [Heading, itemA], [Heading, itemA], [Anchor("listitem", "Item A")]));
    }

    [Fact]
    public void Requires_the_recorded_states_so_a_switch_that_stayed_off_is_not_the_switch_turned_on()
    {
        var on = Anchor("switch", "Email notifications", states: ["checked"]);

        Assert.True(Holds([on], [Node("switch", "Email notifications", states: new NodeStates { Checked = true })]));
        Assert.False(Holds([on], [Node("switch", "Email notifications")]));
        // An anchor recorded without a state does not match the control with one either.
        Assert.False(Holds([Anchor("switch", "Email notifications")], [Node("switch", "Email notifications", states: new NodeStates { Checked = true })]));
    }

    [Fact]
    public void Fails_on_an_alert_the_replay_raised_that_the_recording_never_saw_and_not_on_one_already_there()
    {
        var saved = Anchor("status", "Marker", text: "saved");
        var declined = Node("alert", text: "Card declined");

        Assert.False(Holds([saved], [SavedMarker, declined]));
        Assert.True(Holds([saved], [SavedMarker, declined], [declined]));
        Assert.True(Holds([saved, Anchor("alert", text: "Card declined")], [SavedMarker, declined]));
    }

    [Fact]
    public void Reads_the_volatile_parts_of_an_alert_as_placeholders_both_for_the_recorded_one_and_for_one_already_there()
    {
        static SemanticNode Expiring(string at) => Node("alert", text: "Session expires at " + at);

        Assert.True(Holds([Anchor("alert", text: "Session expires at 17:42")], [Expiring("17:45")]));
        Assert.True(Holds([Anchor("status", "Marker", text: "saved")], [SavedMarker, Expiring("17:45")], [Expiring("17:44")]));
        Assert.False(Holds([Anchor("alert", text: "Session expires at 17:42")], [Node("alert", text: "Session expired")]));
    }

    [Fact]
    public void Forgives_a_churned_test_id_when_the_other_fields_still_identify_the_node()
    {
        var rerendered = Node("link", "PB-Twin-Alpha", testId: "row-9f8e");

        Assert.True(Holds([Anchor("link", "PB-Twin-Alpha", testId: "row-1a2b")], [rerendered]));
        Assert.False(Holds([Anchor("listitem", testId: "row-1a2b")], [rerendered]));
    }
}
