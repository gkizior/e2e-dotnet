// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ExpectSoft;

/// <summary>
/// The port's soft assertions take locator matchers only, so the value-matcher its of upstream's describe are not
/// ported: "throws like expect outside an attempt", "keeps failures on the attempt, lets the caller continue, and fails
/// once with every message in order", "closes with nothing when every soft matcher passed, and a lone failure reads in
/// the singular", "indents a multi-line failure under its number", "unwinds through the first failure, so the error
/// points at the first expect.soft line", "hands each failure out once, so a second close after a timed-out body
/// returns nothing", "keeps a usage failure too, since the matcher could not pass", and "soften keeps an async
/// assertion failure, softens .not, and lets other errors through". The one ported here uses a locator matcher in
/// place of <c>expect.soft(1).toBe(2)</c>, so it checks the throw but not the value matcher's message.
/// </summary>
public sealed class ExpectSoftTests
{
    [Fact]
    public async Task Throws_once_the_collection_is_closed_as_an_afterEach_hook_finds_it()
    {
        await using var session = await StartAsync();
        await session.App.OpenAsync("/settings/billing");
        Assert.Null(session.CloseSoftFailures());
        await Assert.ThrowsAsync<TestException>(() => Expect.Soft(session.Screen.GetByRole("status", "Pro")).ToBeVisibleAsync());
    }

    private static Task<E2ESession> StartAsync()
    {
        return E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(BillingWorld.Create()),
            BaseUrl = "https://billing.test",
            CacheEnabled = false,
            TestTitle = "billing > soft",
            AssertionTimeout = TimeSpan.FromMilliseconds(100),
            ActionTimeout = TimeSpan.FromMilliseconds(300),
            TestTimeout = TimeSpan.FromSeconds(10),
        });
    }
}
