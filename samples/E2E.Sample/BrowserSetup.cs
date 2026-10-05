// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Sample;

/// <summary>Installs the Chromium build Microsoft.Playwright expects, once per run. It does nothing when it is already installed.</summary>
[SetUpFixture]
public sealed class BrowserSetup
{
    [OneTimeSetUp]
    public void InstallChromium()
    {
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Playwright could not install Chromium (exit code {exitCode}).");
        }
    }
}
