// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.Install;

// The install reads and sets PLAYWRIGHT_SKIP_BROWSER_GC on the process, so these tests run apart from the Chromium tests.
[Collection(BrowserCollection.Name)]
public sealed class EnsureBrowsersInstalledTests
{
    private const string SkipBrowserGc = "PLAYWRIGHT_SKIP_BROWSER_GC";

    [Fact]
    public void Does_not_collect_the_browsers_the_declared_cache_holds()
    {
        // The run spawns the install with the cache it was pointed at, and that
        // command removes every revision no installed Playwright declares. Those
        // revisions belong to the other tools sharing that cache.
        Assert.Equal("1", InstallEnvironment(userValue: null));
    }

    [Fact]
    public void Keeps_a_browser_collection_the_caller_asked_for()
    {
        Assert.Equal("0", InstallEnvironment(userValue: "0"));
    }

    [Fact]
    public void Asks_for_the_shell_only_on_a_headless_chromium_run()
    {
        // The assertion the install runs on: a headless chromium run launches the
        // shell, so `--only-shell` keeps the full build out of its download. A headed
        // run launches the full build.
        Assert.Equal(["--only-shell", "chromium"], WebEngine.InstallArgs(headed: false));
        Assert.Equal(["chromium"], WebEngine.InstallArgs(headed: true));
    }

    [Fact]
    public void Passes_the_run_mode_to_the_installer()
    {
        // The port hands the installer the mode as its arguments: a headed run asks for the full build.
        string[] seen = [];
        WebEngine.RunChromiumInstall(headed: true, args =>
        {
            seen = args;
            return 0;
        });
        Assert.Equal(["install", "chromium"], seen);
    }

    /// <summary>Runs the install with a fake installer and returns the PLAYWRIGHT_SKIP_BROWSER_GC it saw.</summary>
    private static string? InstallEnvironment(string? userValue)
    {
        var previous = Environment.GetEnvironmentVariable(SkipBrowserGc);
        Environment.SetEnvironmentVariable(SkipBrowserGc, userValue);
        try
        {
            string? seen = null;
            WebEngine.RunChromiumInstall(headed: false, _ =>
            {
                seen = Environment.GetEnvironmentVariable(SkipBrowserGc);
                return 0;
            });
            return seen;
        }
        finally
        {
            Environment.SetEnvironmentVariable(SkipBrowserGc, previous);
        }
    }
}
