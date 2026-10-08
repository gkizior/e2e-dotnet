// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Tests.TraceAnchors.AnchorFixtures;

namespace E2E.Tests.TraceAnchors;

/// <summary><c>deltaEvidenced</c>: whether the replay itself produced the recorded delta.</summary>
public sealed class DeltaEvidencedTests
{
    [Fact]
    public void Holds_when_some_recorded_change_was_not_already_on_the_screen_the_replay_began_on()
    {
        var delta = new CacheEntry { Appeared = [Anchor("status", text: "Submitted")], Gone = [Anchor("status", text: "Draft")] };

        Assert.True(Anchors.Evidenced(delta, Project([Heading, Node("status", text: "Draft")]), []));
        Assert.True(Anchors.Evidenced(new CacheEntry { Appeared = [Anchor("status", text: "Submitted")] }, Project([Heading]), []));
    }

    [Fact]
    public void Fails_when_the_outcome_already_showed_before_the_first_action_a_submit_that_did_nothing()
    {
        var delta = new CacheEntry { Appeared = [Anchor("status", text: "Submitted")], Gone = [Anchor("status", text: "Draft")] };

        Assert.False(Anchors.Evidenced(delta, Project([Heading, Node("status", text: "Submitted")]), []));
    }

    [Fact]
    public void Fails_for_a_recording_with_no_delta_unless_the_last_action_moved_the_route_leaving_no_baseline_on_the_end_route()
    {
        Assert.False(Anchors.Evidenced(new CacheEntry(), Project([Heading]), []));
        Assert.True(Anchors.Evidenced(new CacheEntry(), null, []));
    }

    [Fact]
    public void Counts_a_value_the_step_typed_only_when_the_step_changed_nothing_else()
    {
        var email = Anchor("textbox", "Email");
        var typed = Anchor("textbox", "Email", value: "ada@example.test");
        var subscribed = Node("status", text: "You're subscribed");
        var form = new CacheEntry { Appeared = [typed, Anchor("status", text: "You're subscribed")], Gone = [email] };

        // The page already read the outcome when the replay typed: the typed value alone proves nothing.
        Assert.False(Anchors.Evidenced(form, Project([Heading, Node("textbox", "Email"), subscribed]), [email]));
        Assert.True(Anchors.Evidenced(form, Project([Heading, Node("textbox", "Email")]), [email]));
        // A step that only fills a field has nothing but the value to show.
        Assert.True(Anchors.Evidenced(new CacheEntry { Appeared = [typed], Gone = [email] }, Project([Heading, Node("textbox", "Email")]), [email]));
    }
}
