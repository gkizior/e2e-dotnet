// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Mobile;

public sealed partial class DeviceSession
{
    /// <summary>
    /// One pointer action at a screen point in logical pixels, the space every node's bounds are in, with no element
    /// resolved behind it. A test's step carries no settle wait, as the node taps do not.
    /// </summary>
    public async Task PerformAtAsync(Point point, PointerAction action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        var before = _latestIndex;
        await CommandAsync(
            $"{PointerLabel(action)} at point",
            (client, token) => DeviceActions.PointerInteractionAsync(client, point, action, ActionSettle(), token),
            cancellationToken).ConfigureAwait(false);
        MarkAction(before);
    }

    /// <summary>
    /// Types into whatever holds focus through the device's text-input path: agent-device's <c>type</c> lands on the
    /// focused field. Replacing is not a device primitive without a target, so it is refused rather than faked: the model
    /// clears a listed field by filling it by id.
    /// </summary>
    public async Task TypeTextAsync(string text, bool replace = false, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (replace)
        {
            throw DeviceSupport.Unsupported("agent-device cannot clear the focused field without a node; type into a listed field by id to replace its value");
        }

        var before = _latestIndex;
        await CommandAsync("keyboard.type", DeviceMethods.InteractionsType, DeviceArgs.Of(("text", JsonValue.Create(text))), cancellationToken).ConfigureAwait(false);
        MarkAction(before);
    }

    /// <summary>A hold on a node. Without a duration it holds <see cref="DeviceActions.DefaultLongPress"/>.</summary>
    public Task LongPressAsync(SemanticNode node, TimeSpan? duration = null, CancellationToken cancellationToken = default) =>
        DeviceActions.LongPressAsync(this, node, duration, cancellationToken);

    /// <summary>A pointer hover over a node.</summary>
    public Task HoverAsync(SemanticNode node, CancellationToken cancellationToken = default) =>
        DeviceActions.HoverAsync(this, node, cancellationToken);

    /// <summary>A drag from one node to another.</summary>
    public Task DragToAsync(SemanticNode node, SemanticNode destination, CancellationToken cancellationToken = default) =>
        DeviceActions.DragToAsync(this, node, destination, cancellationToken);

    /// <summary>A swipe inside a node's bounds, as far as <paramref name="momentum"/> says. On the screen root it scrolls the whole viewport.</summary>
    public Task SwipeAsync(SemanticNode node, ScrollDirection direction, Momentum? momentum = null, CancellationToken cancellationToken = default) =>
        DeviceActions.SwipeAsync(this, node, direction, momentum, cancellationToken);

    private static string PointerLabel(PointerAction action) => action switch
    {
        PointerAction.Tap => "tap",
        PointerAction.DoubleTap => "doubleTap",
        PointerAction.LongPress => "longPress",
        PointerAction.SwipeTo => "swipeTo",
        PointerAction.SecondaryTap => "secondaryTap",
        PointerAction.Hover => "hover",
        PointerAction.DragTo => "dragTo",
        _ => "swipe",
    };
}
