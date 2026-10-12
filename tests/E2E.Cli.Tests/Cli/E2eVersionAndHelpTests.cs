// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Cli.Tests.Cli;

/// <summary>
/// The port's CLI has no <c>--version</c>, no per-command help, and no <c>run</c> flags. So these upstream its are not
/// ported: "prints the package version alone and exits 0" and "groups the run flags, then lists examples and the exit
/// codes".
/// </summary>
public sealed class E2eVersionAndHelpTests
{
    /// <summary>Partial: the port's help lists its five commands and the providers, with no version line, examples, or docs link.</summary>
    [Fact]
    public async Task Opens_the_help_with_the_usage_and_every_command_and_exits_0()
    {
        var (code, stdout, _) = await InvokeAsync("--help");

        Assert.Equal(0, code);
        Assert.StartsWith("Usage:", stdout, StringComparison.Ordinal);
        foreach (var command in new[] { "e2e login", "e2e logout", "e2e models", "e2e guide", "e2e mcp" })
        {
            Assert.Contains(command, stdout, StringComparison.Ordinal);
        }
    }

    /// <summary>Partial: the port has no per-command help, so <c>-h</c> and <c>help</c> answer with the same overview as <c>--help</c>.</summary>
    [Fact]
    public async Task Answers_h_and_help_with_the_same_text_as_help()
    {
        var (_, expected, _) = await InvokeAsync("--help");

        foreach (var args in new[] { new[] { "-h" }, ["help"] })
        {
            var (code, stdout, stderr) = await InvokeAsync(args);
            Assert.Equal(expected, stdout);
            Assert.Equal("", stderr);
            Assert.Equal(0, code);
        }
    }

    /// <summary>Partial: the port prints the usage on stdout and exits 1, where upstream prints it on stderr and exits 2.</summary>
    [Fact]
    public async Task Prints_the_usage_on_stdout_and_exits_1_when_no_command_is_given()
    {
        var (code, stdout, stderr) = await InvokeAsync();

        Assert.Equal(1, code);
        Assert.Contains("Usage:", stdout, StringComparison.Ordinal);
        Assert.Equal("", stderr);
    }

    private static async Task<(int Code, string Stdout, string Stderr)> InvokeAsync(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var code = await Program.RunAsync(args, stdout, stderr, TextReader.Null, interactive: false, CancellationToken.None);
        return (code, stdout.ToString(), stderr.ToString());
    }
}
