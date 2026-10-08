// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Cli.Mcp;
using static E2E.Cli.Tests.McpTools.ToolFixtures;

namespace E2E.Cli.Tests.McpTools;

public sealed class ErrorResultTests
{
    [Fact]
    public void Prefixes_runner_errors_with_their_code_and_leaves_foreign_errors_as_their_message()
    {
        var coded = Tools.ErrorResult(new TestException("ACTION_FAILED", "covered"));
        var foreign = Tools.ErrorResult(new InvalidOperationException("boom"));

        Assert.Equal([("text", "ACTION_FAILED: covered")], Parts(coded));
        Assert.True(coded.IsError);
        Assert.Equal([("text", "boom")], Parts(foreign));
        Assert.True(foreign.IsError);
    }
}
