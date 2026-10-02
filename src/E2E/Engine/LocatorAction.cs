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

    public sealed record Fill(string Value, bool Sensitive = false) : LocatorAction;

    public sealed record Press(string Key) : LocatorAction;

    public sealed record Select(string Value) : LocatorAction;

    public sealed record Check : LocatorAction;

    public sealed record Uncheck : LocatorAction;

    public sealed record Clear : LocatorAction;
}
