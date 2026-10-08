// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.AgentObservation;

/// <summary>
/// <c>observationShape</c>: what two looks are compared by. The lines use the port's screen text, where a node
/// reference reads <c>[ref=e1]</c> and each state is its own bracket. Upstream's "is shaped by the pixels when a
/// capture carries them" is not ported: the port's observations carry no pixels.
/// </summary>
public sealed class ObservationShapeTests
{
    private static string ShapeOf(string text) => global::E2E.AgentObservation.Shape(text);

    [Fact]
    public void Ignores_the_per_observation_node_ids()
    {
        Assert.Equal(
            ShapeOf("- button \"Save\" [ref=e1]\n  - link \"Home\" [ref=e2]"),
            ShapeOf("- button \"Save\" [ref=e9]\n  - link \"Home\" [ref=e7]"));
    }

    [Fact]
    public void Ignores_focus_which_moves_without_the_page_changing()
    {
        // The browser settling focus after load, a script claiming it, a widget
        // stealing it: none of it changes what a judgment would answer.
        Assert.Equal(ShapeOf("- textbox \"Email\" [ref=e1] [focused]"), ShapeOf("- textbox \"Email\" [ref=e1]"));
        Assert.Equal(ShapeOf("- checkbox \"Terms\" [ref=e1] [checked] [focused]"), ShapeOf("- checkbox \"Terms\" [ref=e1] [checked]"));
    }

    [Fact]
    public void Still_notices_a_state_that_is_about_the_page()
    {
        Assert.NotEqual(ShapeOf("- checkbox \"Terms\" [ref=e1] [checked]"), ShapeOf("- checkbox \"Terms\" [ref=e1]"));
        Assert.NotEqual(ShapeOf("- button \"Save\" [ref=e1] [disabled]"), ShapeOf("- button \"Save\" [ref=e1]"));
    }

    [Fact]
    public void Ignores_clock_like_values_which_tick_without_the_page_changing()
    {
        // A timer would end the wait for an action's effect on its first tick and
        // keep a settle from ever seeing two looks agree.
        Assert.Equal(ShapeOf("- status \"Elapsed 00:12\" [ref=e1]"), ShapeOf("- status \"Elapsed 00:13\" [ref=e1]"));
        Assert.Equal(ShapeOf("- text \"12:05:59\" [ref=e1]"), ShapeOf("- text \"12:06:00\" [ref=e1]"));
        Assert.NotEqual(ShapeOf("- status \"Items 12\" [ref=e1]"), ShapeOf("- status \"Items 13\" [ref=e1]"));
    }

    [Fact]
    public void Notices_changed_text_and_changed_structure()
    {
        Assert.NotEqual(ShapeOf("- status \"Loading\" [ref=e1]"), ShapeOf("- status \"Ready\" [ref=e1]"));
        Assert.NotEqual(
            ShapeOf("- list [ref=e1]\n  - listitem \"A\" [ref=e2]"),
            ShapeOf("- list [ref=e1]\n  - listitem \"A\" [ref=e2]\n  - listitem \"B\" [ref=e3]"));
    }
}
