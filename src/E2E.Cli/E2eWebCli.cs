// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Cli;

/// <summary>What <see cref="E2eWebCli.MainAsync"/> writes to and runs; tests replace them (upstream <c>CliIo</c>).</summary>
internal sealed record CliIo(TextWriter Stdout, TextWriter Stderr, Func<string[], int> Run);

/// <summary>
/// Upstream's <c>e2e-web</c> command (<c>packages/web/src/cli.ts</c>): browser installs with the Playwright version
/// this package pins. Upstream's bin is <c>e2e-web</c> in the web package. The port has one tool, so it is
/// <c>e2e install</c>, and <see cref="Program"/> hands it the arguments after <c>install</c>.
/// </summary>
internal static class E2eWebCli
{
    private static readonly string BrowserArgs = "[" + string.Join('|', BrowserConnection.BrowserNames) + " ...] [--with-deps]";

    internal static readonly string Usage = $"""
        Usage: e2e install {BrowserArgs}
               dotnet e2e install {BrowserArgs}

        Installs browsers for the Playwright version E2E runs. Installs chromium
        when no browser is named. --with-deps also installs the system libraries
        the browsers need on Linux.
        """;

    /// <summary>Runs <c>e2e-web install</c> with the arguments after <c>install</c> and returns the exit code (upstream <c>main</c>; a method named <c>Main</c> would be read as an entry point).</summary>
    internal static async Task<int> MainAsync(string[] args, CliIo io)
    {
        if (args.Any(arg => arg is "-h" or "--help"))
        {
            await io.Stdout.WriteLineAsync(Usage).ConfigureAwait(false);
            return 0;
        }

        var options = args.Where(arg => arg.StartsWith('-')).ToList();
        var names = args.Where(arg => !arg.StartsWith('-')).ToList();
        if (options.Find(option => option != "--with-deps") is { } unknownOption)
        {
            await io.Stderr.WriteLineAsync("unknown option \"" + unknownOption + "\"\n\n" + Usage).ConfigureAwait(false);
            return 2;
        }

        if (names.Find(name => !BrowserConnection.IsBrowserName(name)) is { } unknownBrowser)
        {
            await io.Stderr.WriteLineAsync("unknown browser \"" + unknownBrowser + "\"; expected one of " + string.Join(", ", BrowserConnection.BrowserNames)).ConfigureAwait(false);
            return 2;
        }

        return io.Run(["install", .. options, .. names.Count > 0 ? names : ["chromium"]]);
    }
}
