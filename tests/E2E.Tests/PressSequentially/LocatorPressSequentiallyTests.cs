// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Tests.ScriptedScreen;

namespace E2E.Tests.PressSequentially;

/// <summary>
/// <c>locator.pressSequentially</c>. Upstream composes it in core from a focus action and a keyboard capability, and
/// the tests read the engine's keyboard log. The port hands one <c>PressSequentially</c> action to the engine, which
/// focuses and types, so these tests read the actions the scene received. "cuts a pause at the action deadline and sends
/// no character past it" is not ported: the port gives the engine the whole text and the delay, and a page operation
/// that outlives the deadline fails with <c>ACTION_MAY_HAVE_COMMITTED</c>, not <c>ACTION_FAILED</c> naming the characters
/// typed. "splits by code point, so a surrogate pair is one keystroke" is not ported: the document engine appends one UTF-16 character at a time, so a surrogate pair fires two fill hooks.
/// "refuses a target whose engine has no keyboard before resolving any node" and "refuses a target whose engine
/// does not declare the focus action" are not ported: the port has no per-action capability list and does not check the
/// keyboard capability. "refuses a Secret with INVALID_ARGUMENT and points at fill" is not ported: the typed API has no
/// <c>Secret</c> overload, so the call does not compile. The non-string and <c>delayMs</c> cases of the last <c>it</c> cannot be written in C#.
/// </summary>
public sealed class LocatorPressSequentiallyTests
{
    private static ScriptedScene CityScene() =>
        new(_ => [new ScriptedNode { Id = "city", Role = "textbox", Name = "City" }]);

    [Fact]
    public async Task Hands_the_whole_text_to_the_engine_as_one_action_on_the_one_matching_node()
    {
        using var scene = CityScene();
        scene.Open();

        await scene.Screen.GetByLabel("City").PressSequentiallyAsync("Warsaw");

        var (id, action) = Assert.Single(scene.Performs);
        Assert.Equal("city", id);
        Assert.Equal(new LocatorAction.PressSequentially("Warsaw", null), action);
    }

    [Fact]
    public async Task Hands_the_delay_to_the_engine_with_the_text_for_a_delayed_type()
    {
        using var scene = CityScene();
        scene.Open();

        await scene.Screen.GetByLabel("City").PressSequentiallyAsync("Wa", new PressSequentiallyOptions { Delay = TimeSpan.FromMilliseconds(60) });

        var (_, action) = Assert.Single(scene.Performs);
        Assert.Equal(new LocatorAction.PressSequentially("Wa", TimeSpan.FromMilliseconds(60)), action);
    }

    [Fact]
    public async Task Rejects_a_null_text_and_a_negative_delay_before_any_node_resolves()
    {
        using var scene = CityScene();
        scene.Open();
        var city = scene.Screen.GetByLabel("City");

        await Assert.ThrowsAsync<ArgumentNullException>(() => city.PressSequentiallyAsync(null!));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => city.PressSequentiallyAsync("W", new PressSequentiallyOptions { Delay = TimeSpan.FromMilliseconds(-1) }));
        Assert.Empty(scene.Performs);
    }
}
