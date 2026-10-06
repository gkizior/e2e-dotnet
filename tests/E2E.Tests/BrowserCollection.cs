// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests;

/// <summary>
/// Runs the Chromium tests one after another. <see cref="E2E.Engine.WebEngine"/> installs Chromium on the first launch.
/// </summary>
[CollectionDefinition(Name)]
public sealed class BrowserCollection
{
    public const string Name = "Browser";
}
