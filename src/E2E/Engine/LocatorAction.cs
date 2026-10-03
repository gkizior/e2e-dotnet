// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Engine;

/// <summary>A deterministic action <see cref="IEngineSession.PerformAsync"/> carries out on one node.</summary>
public abstract record LocatorAction
{
    private LocatorAction()
    {
    }

    public sealed record Tap : LocatorAction;

    public sealed record DoubleTap : LocatorAction;

    public sealed record Fill(string Value, bool Sensitive = false) : LocatorAction;

    public sealed record Press(string Key) : LocatorAction;

    /// <summary>Focuses the node and types <paramref name="Text"/> one character at a time.</summary>
    public sealed record PressSequentially(string Text, TimeSpan? Delay = null) : LocatorAction;

    public sealed record Select(string Value) : LocatorAction;

    public sealed record Check : LocatorAction;

    public sealed record Uncheck : LocatorAction;

    public sealed record Clear : LocatorAction;

    public sealed record Focus : LocatorAction;

    /// <summary>Scrolls the node's nearest scrollable container until the node is in the viewport.</summary>
    public sealed record ScrollIntoView : LocatorAction;

    /// <summary>Scrolls a scrollable node about three quarters of its height or width. The viewport swipe is <see cref="IEngineSession.SwipeAsync"/>.</summary>
    public sealed record Swipe(ScrollDirection Direction) : LocatorAction;
}

/// <summary>Which way a scroll moves the content into view. <see cref="Down"/> reveals what is below.</summary>
public enum ScrollDirection
{
    Up,
    Down,
    Left,
    Right,
}
