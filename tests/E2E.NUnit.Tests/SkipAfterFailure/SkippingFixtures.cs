// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E;
using NUnit.Framework.Api;
using NUnit.Framework.Interfaces;
using NUnit.Framework.Internal;

namespace E2E.NUnit.Tests.SkipAfterFailure;

/// <summary>
/// Runs one of the <see cref="SkippingFixture"/> tests in a runner of its own and reads its result,
/// since the tests under check end failed or skipped, which the running test cannot assert on itself.
/// The fixtures are explicit, so a normal run never starts them.
/// </summary>
internal static class NestedRun
{
    public static ITestResult Run(string testName)
    {
        // A runner of its own, on a thread of its own, so the running test's context stays out of it.
        return Task.Run(() =>
        {
            var runner = new NUnitTestAssemblyRunner(new DefaultTestAssemblyBuilder());
            runner.Load(typeof(NestedRun).Assembly, new Dictionary<string, object>());
            return Leaf(runner.Run(TestListener.NULL, TestFilter.FromXml($"<filter><name>{testName}</name></filter>")));
        }).GetAwaiter().GetResult();
    }

    // The runner answers with the assembly's result. The test is the one result with no children.
    private static ITestResult Leaf(ITestResult result)
    {
        return result.HasChildren ? Leaf(result.Children.Single(child => child.Test.IsSuite || child.Test.RunState != RunState.NotRunnable)) : result;
    }
}

/// <summary>The tests of upstream's <c>skip-after-failure.test.ts</c>, as an NUnit body would write them.</summary>
[Explicit("Run by ARuntimeSkipAfterAFailureTests")]
public sealed class SkippingFixture : E2ETest
{
    private static readonly E2EConfig FixtureConfig = E2EConfig.Parse(
        """{ "cache": { "mode": "off" } }""",
        AppContext.BaseDirectory,
        _ => null);

    private static readonly TimeSpan SoftTimeout = TimeSpan.FromMilliseconds(200);

    public static int Attempts;

    public static bool TeardownRan;

    public static List<string?> Messages { get; } = [];

    protected override E2EConfig Config => FixtureConfig;

    protected override string? BaseUrl => E2E.Playground.Testbed.Url;

    [SetUp]
    public async Task OpenBrowserPageAsync()
    {
        Attempts++;
        await App.OpenAsync("/browser");
    }

    [TearDown]
    public void RecordTeardown()
    {
        TeardownRan = true;
        Messages.Add(global::NUnit.Framework.TestContext.CurrentContext.Result.Message);
    }

    [Test]
    public async Task Soft_then_skip()
    {
        await Expect.Soft(Screen.GetByLabel("Loads")).ToHaveTextAsync("the count", timeout: SoftTimeout);
        await Expect.Soft(Screen.GetByLabel("Random")).ToHaveTextAsync("the version", timeout: SoftTimeout);
        Assert.Ignore("feature disabled");
    }

    [Test]
    public async Task Soft_then_inconclusive()
    {
        await Expect.Soft(Screen.GetByLabel("Loads")).ToHaveTextAsync("the count", timeout: SoftTimeout);
        Assert.Inconclusive("feature disabled");
    }

    [Test]
    [Retry(3)]
    public async Task Soft_then_skip_with_retries()
    {
        await Expect.Soft(Screen.GetByLabel("Loads")).ToHaveTextAsync("the count", timeout: SoftTimeout);
        Assert.Ignore("feature disabled");
    }

    [Test]
    [Retry(2)]
    public async Task Soft_skip_then_clean_skip()
    {
        if (Attempts == 1)
        {
            await Expect.Soft(Screen.GetByLabel("Loads")).ToHaveTextAsync("the count", timeout: SoftTimeout);
        }

        Assert.Ignore("feature disabled");
    }

    [Test]
    public async Task Clean_skip()
    {
        await Expect.Soft(Screen.GetByLabel("Loads")).ToHaveTextAsync("loads: 1", timeout: SoftTimeout);
        Assert.Ignore("not applicable");
    }

    [Test]
    public async Task False_condition()
    {
        var disabled = Attempts < 0;
        if (disabled)
        {
            Assert.Ignore("not applicable");
        }

        await Expect.That(Screen.GetByLabel("Loads")).ToHaveTextAsync("loads: 1");
    }

    [Test]
    [Retry(2)]
    public async Task Soft_failure_then_recovery()
    {
        if (Attempts == 1)
        {
            await Expect.Soft(Screen.GetByLabel("Loads")).ToHaveTextAsync("the count", timeout: SoftTimeout);
        }
    }
}
