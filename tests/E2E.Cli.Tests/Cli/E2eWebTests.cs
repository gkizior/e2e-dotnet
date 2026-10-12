// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Cli.Tests.Cli;

// Upstream: packages/web/tests/unit/cli.test.ts, describe('e2e-web'). The port's command is `e2e install`.
public sealed class E2eWebTests
{
    [Fact]
    public async Task Installs_chromium_when_no_browser_is_named()
    {
        var result = await RunAsync(["install"]);
        Assert.Equal(0, result.Code);
        Assert.Equal([["install", "chromium"]], result.Calls);
    }

    [Fact]
    public async Task Forwards_the_named_browsers_and_with_deps()
    {
        var result = await RunAsync(["install", "webkit", "--with-deps", "firefox"]);
        Assert.Equal([["install", "--with-deps", "webkit", "firefox"]], result.Calls);
    }

    [Fact]
    public async Task Exits_with_the_Playwright_CLIs_code()
    {
        Assert.Equal(1, (await RunAsync(["install"], exitCode: 1)).Code);
    }

    [Theory]
    [InlineData("install", "--help")]
    [InlineData("install", "webkit", "-h")]
    public async Task Prints_usage_for_install_help_without_running_anything(params string[] args)
    {
        var result = await RunAsync(args);
        Assert.Equal(0, result.Code);
        Assert.Empty(result.Calls);
        Assert.Contains("Usage: e2e install [chromium|firefox|webkit ...] [--with-deps]", result.Stdout, StringComparison.Ordinal);
        Assert.Contains("dotnet e2e install", result.Stdout, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--force", "unknown option \"--force\"")]
    [InlineData("chrome", "unknown browser \"chrome\"; expected one of chromium, firefox, webkit")]
    public async Task Rejects_an_unknown_option_or_browser_with_exit_code_2(string arg, string message)
    {
        var result = await RunAsync(["install", arg]);
        Assert.Equal(2, result.Code);
        Assert.Empty(result.Calls);
        Assert.Contains(message, result.Stderr, StringComparison.Ordinal);
    }

    /// <summary>Runs <c>e2e</c> with recorded output and a fake Playwright CLI that exits with <paramref name="exitCode"/>.</summary>
    private static async Task<(int Code, List<string[]> Calls, string Stdout, string Stderr)> RunAsync(string[] args, int exitCode = 0)
    {
        var calls = new List<string[]>();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = await E2eWebCli.MainAsync(args[1..], new CliIo(stdout, stderr, forwarded =>
        {
            calls.Add(forwarded);
            return exitCode;
        }));
        return (code, calls, stdout.ToString(), stderr.ToString());
    }
}
