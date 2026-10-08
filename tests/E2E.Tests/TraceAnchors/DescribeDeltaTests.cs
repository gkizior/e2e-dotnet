// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Engine;
using static E2E.Tests.TraceAnchors.AnchorFixtures;

namespace E2E.Tests.TraceAnchors;

/// <summary><c>describeDelta</c>: the step's delta, both what appeared and what vanished.</summary>
public sealed class DescribeDeltaTests
{
    [Fact]
    public void Keeps_what_appeared_and_what_vanished_compared_by_descriptor_rather_than_by_id()
    {
        // Same heading, freshly minted ref: not a delta.
        var (appeared, gone) = Describe([Heading, EmptyMarker], [Node("heading", "Playbooks"), SavedMarker, Row, Toast]);

        Assert.Equal(["status|Marker~saved", "link|PB-Twin-Alpha", "|~Playbook saved"], Shapes(appeared));
        Assert.Equal(["status|Marker~empty"], Shapes(gone));
    }

    [Fact]
    public void Records_a_removal_as_what_vanished_so_a_delete_has_something_to_check()
    {
        var itemA = Node("listitem", "Item A");
        var itemB = Node("listitem", "Item B");

        var (appeared, gone) = Describe([Heading, itemA, itemB], [Heading, itemB]);

        Assert.Empty(appeared);
        Assert.Equal(["listitem|Item A"], Shapes(gone));
    }

    [Fact]
    public void Records_the_states_a_step_set_on_a_control_it_left_on_screen()
    {
        var off = Node("switch", "Email notifications", states: new NodeStates { Checked = false, Focused = false });
        var on = Node("switch", "Email notifications", states: new NodeStates { Checked = true, Focused = true });

        // Focus is where the pointer went, not what the step did.
        var (appeared, gone) = Describe([Heading, off], [Heading, on]);

        Assert.Equal(["switch|Email notifications|checked"], Shapes(appeared));
        Assert.Equal(["switch|Email notifications"], Shapes(gone));
    }

    [Fact]
    public void Strips_the_structural_selector_and_drops_nodes_nothing_could_relocate()
    {
        // The port has no structural selector, so the icon button is a button with nothing else.
        var (appeared, _) = Describe([], [Row, Node("button"), Node("generic")]);

        Assert.Equal(["link|PB-Twin-Alpha"], Shapes(appeared));
        Assert.DoesNotContain("selector", JsonSerializer.Serialize(appeared), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deduplicates_repeated_descriptors_and_caps_each_side_at_the_anchor_limit_in_document_order()
    {
        var list = Enumerable.Range(0, Anchors.Max + 4).Select(index => Node("listitem", text: "Row " + index)).ToList();
        var end = list.Append(Node("listitem", text: "Row 0")).ToList();

        var (appeared, _) = Describe([], end);

        Assert.Equal(Anchors.Max, appeared.Count);
        Assert.Equal("listitem|~Row 0", Shape(appeared[0]));
        Assert.Equal("listitem|~Row " + (Anchors.Max - 1), Shape(appeared[^1]));
        Assert.Equal(Shapes(appeared), Shapes(Describe(end, []).Gone));
    }

    [Fact]
    public void Records_the_value_a_step_typed_into_a_field_redacted_and_never_a_secure_fields()
    {
        static string Redact(string value) => value.Replace("tok_9f8e7d6c5b4a3210", "<secret:token>", StringComparison.Ordinal);
        var empty = Node("textbox", "Email");
        var typed = Node("textbox", "Email", value: "ada@example.test");
        var leaked = Node("textbox", "Key", value: "tok_9f8e7d6c5b4a3210");

        var (appeared, gone) = Anchors.Describe(Project([empty], Redact), Project([typed, leaked], Redact), false);

        Assert.Equal(["textbox|Email=ada@example.test", "textbox|Key=<secret:token>"], Shapes(appeared));
        Assert.Equal(["textbox|Email"], Shapes(gone));
    }

    [Fact]
    public void Never_carries_a_secure_value_or_secret_plaintext()
    {
        static string Redact(string value) => value.Replace("hunter2", "<secret:password>", StringComparison.Ordinal);
        var password = Node("textbox", "Password", value: "hunter2", states: new NodeStates { Secure = true });
        var echo = Node(text: "you typed hunter2");

        var (appeared, _) = Anchors.Describe(Project([], Redact), Project([password, echo], Redact), false);

        Assert.DoesNotContain("hunter2", JsonSerializer.Serialize(appeared), StringComparison.Ordinal);
        Assert.Equal(["textbox|Password", "|~you typed <secret:password>"], Shapes(appeared));
    }

    [Fact]
    public void Puts_announcements_first_then_leaves_then_containers_which_only_repeat_their_children()
    {
        // Ten list items, each a container over one text leaf, plus a status line after the list:
        // 21 new nodes for a cap of 8.
        var roots = Enumerable.Range(0, 10)
            .Select(index => Node("listitem", "Todo " + index + "Delete Todo " + index, children: [Node(text: "Todo " + index)]))
            .Append(Node("status", text: "Report ready"))
            .ToList();

        var (appeared, _) = Describe([], roots);

        Assert.Equal(Anchors.Max, appeared.Count);
        // The app's own announcement of the outcome is never what the cap cuts.
        Assert.Equal("status|~Report ready", Shape(appeared[0]));
        Assert.Equal(Enumerable.Range(0, Anchors.Max - 1).Select(index => "|~Todo " + index), Shapes(appeared.Skip(1)));
    }

    [Fact]
    public void Does_not_mistake_a_re_minted_test_id_for_a_new_node()
    {
        var before = Node("button", "Start sync", testId: "toggle-r1-2");
        var after = Node("button", "Start sync", testId: "toggle-r2-2");
        var effect = Node(text: "Activate plan is on");

        Assert.Equal(["|~Activate plan is on"], Shapes(Describe([before], [after, effect]).Appeared));

        // A node only its test id identifies still counts by that id.
        var idOnly = Node("generic", testId: "spinner");
        Assert.Equal(["generic|#spinner"], Shapes(Describe([], [idOnly]).Appeared));
    }

    [Fact]
    public void Skips_text_that_cannot_read_the_same_twice_while_a_stable_anchor_remains()
    {
        SemanticNode[] end =
        [
            Heading,
            Node(text: "sk_b1bccf4e03c5_..."),
            Node(text: "6 days 23 hours remaining"),
            Node(text: "Added Sep 8, 2026"),
            Node(text: "2026-09-08"),
            Node(text: "17:42"),
            Node(text: "Done in 321ms"),
            Node(text: "11"),
            Node(text: "Release pipeline"),
        ];

        Assert.Equal(["|~Release pipeline"], Shapes(Describe([Heading], end).Appeared));
    }

    [Fact]
    public void Keeps_volatile_anchors_when_nothing_stable_appeared_so_the_replay_hands_off_rather_than_passing_blind()
    {
        Assert.Equal(["|~sk_b1bccf4e03c5_..."], Shapes(Describe([Heading], [Heading, Node(text: "sk_b1bccf4e03c5_...")]).Appeared));
    }

    [Fact]
    public void Reads_an_alert_whose_countdown_ticked_during_the_step_as_the_alert_that_stayed()
    {
        static SemanticNode Banner(int minutes) => Node("alert", text: "Session expires in " + minutes + " minutes");

        var (appeared, gone) = Describe([Heading, Banner(5)], [Heading, Banner(4), Toast]);

        Assert.Equal(["|~Playbook saved"], Shapes(appeared));
        Assert.Empty(gone);
    }

    [Fact]
    public void Keeps_every_alert_whatever_its_text_since_a_replay_is_checked_for_alerts_it_raised()
    {
        var (appeared, _) = Describe([Heading], [Heading, Node("alert", text: "Session expires at 17:42"), Node(text: "Release pipeline")]);

        Assert.Equal(["alert|~Session expires at 17:42", "|~Release pipeline"], Shapes(appeared));
    }

    [Fact]
    public void Does_not_mistake_progress_versions_short_ids_or_a_named_counter_for_volatile_text()
    {
        var (appeared, _) = Describe(
            [Heading],
            [Heading, Node(text: "3 / 30 steps"), Node(text: "v2.2.1"), Node(text: "E2E workspace 00d8365e"), Node("status", "Counter", text: "1")]);

        Assert.Equal(4, appeared.Count);
    }

    [Fact]
    public void Keeps_a_count_the_step_made_appear_which_is_its_result_and_skips_one_that_only_moved_or_that_came_with_another_route_which_is_the_data()
    {
        SemanticNode[] imported = [Heading, Node("status", text: "3 records imported"), Node(text: "Showing 1 to 9 of 9 results")];
        Assert.Equal(["status|~3 records imported", "|~Showing 1 to 9 of 9 results"], Shapes(Describe([Heading], imported).Appeared));

        // The same screen opened by a link: its counts are what the list holds today.
        Assert.Equal(["|~Imported records"], Shapes(Describe([Heading], [.. imported, Node(text: "Imported records")], routeMoved: true).Appeared));

        // A tally that was there before the step and reads another number after it grows with every run's leftovers.
        var (appeared, gone) = Describe([Heading, Node(text: "41 items")], [Heading, Node(text: "42 items"), Node(text: "Item added")]);
        Assert.Equal(["|~Item added"], Shapes(appeared));
        Assert.Equal(["|~41 items"], Shapes(gone));
    }

    [Fact]
    public void Is_empty_when_nothing_changed()
    {
        var (appeared, gone) = Describe([Heading, EmptyMarker], [Heading, EmptyMarker]);

        Assert.Empty(appeared);
        Assert.Empty(gone);
    }
}
