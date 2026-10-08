// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests;

/// <summary>
/// A screen over a fixed node list, as upstream's <c>createScreenFixture</c>
/// builds one, so locator reads and matchers run without a browser.
/// </summary>
internal static class ScreenFixture
{
    /// <summary>Action and assertion timeout of the fixture, short enough for failing-path tests.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(300);

    public static Screen Create(params SemanticNode[] nodes)
    {
        var observation = new Observation { Route = "/", Roots = nodes };
        return new Screen(
            _ => Task.FromResult(observation),
            (_, _, _) => Task.CompletedTask,
            () => CancellationToken.None,
            () => { },
            Timeout,
            Timeout);
    }
}
