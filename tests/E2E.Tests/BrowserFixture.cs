// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests;

/// <summary>
/// Installs the Chromium build that Microsoft.Playwright expects, once per test run, before the first
/// Chromium test. It does nothing when that build is already installed.
/// </summary>
public sealed class BrowserFixture
{
    public BrowserFixture()
    {
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Playwright could not install Chromium (exit code {exitCode}).");
        }
    }
}

[CollectionDefinition(Name)]
public sealed class BrowserCollection : ICollectionFixture<BrowserFixture>
{
    public const string Name = "Browser";
}
