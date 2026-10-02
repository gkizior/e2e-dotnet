// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests;

public sealed class SessionTests
{
    [Fact]
    public async Task Complete_records_a_verified_act_for_the_next_session()
    {
        var directory = TempCache();
        var firstCalls = 0;
        await using (var session = await StartAsync(directory, () => firstCalls++, attempt: 1))
        {
            await UpgradeAsync(session);
            session.Complete();
            Assert.True(firstCalls > 0);
        }

        var secondCalls = 0;
        await using (var session = await StartAsync(directory, () => secondCalls++, attempt: 1))
        {
            await UpgradeAsync(session);
            session.Complete();
            Assert.Equal(0, secondCalls);
            Assert.Equal(1, session.Replayed);
        }
    }

    [Fact]
    public async Task A_failed_session_does_not_replay()
    {
        var directory = TempCache();
        await using (var session = await StartAsync(directory, () => { }, attempt: 1))
        {
            await session.App.OpenAsync("/settings/billing");
            await session.Agent.ActAsync("upgrade the workspace to the Pro plan");
            session.Complete(new TestException("TEST_FAILED", "stopped before the check"));
        }

        var calls = 0;
        await using (var session = await StartAsync(directory, () => calls++, attempt: 1))
        {
            await UpgradeAsync(session);
            session.Complete();
            Assert.True(calls > 0);
            Assert.Equal(0, session.Replayed);
        }
    }

    [Fact]
    public async Task A_later_attempt_does_not_replay()
    {
        var directory = TempCache();
        await using (var session = await StartAsync(directory, () => { }, attempt: 1))
        {
            await UpgradeAsync(session);
            session.Complete();
        }

        var calls = 0;
        await using (var session = await StartAsync(directory, () => calls++, attempt: 2))
        {
            await UpgradeAsync(session);
            session.Complete();
            Assert.True(calls > 0);
            Assert.Equal(0, session.Replayed);
        }
    }

    private static async Task UpgradeAsync(E2ESession session)
    {
        await session.App.OpenAsync("/settings/billing");
        await session.Agent.ActAsync("upgrade the workspace to the Pro plan");
        await session.Agent.AssertAsync("the invoice preview shows a prorated amount");
    }

    private static Task<E2ESession> StartAsync(string directory, Action onAct, int attempt)
    {
        var world = new DocumentWorld().Map("/settings/billing", page =>
        {
            page.Heading("Billing");
            var status = page.Status("Pro", hidden: true);
            var invoice = page.Paragraph("Invoice preview");
            page.Button("Upgrade to Pro", () =>
            {
                status.Hidden = false;
                invoice.Text = "Prorated amount: $12";
            });
        });
        return E2ESession.StartAsync(new E2ESessionOptions
        {
            Engine = new DocumentEngine(world),
            Model = new ScriptedModel(request =>
            {
                var text = string.Join('\n', request.Messages.Select(message => message.Content));
                if (text.Contains("Statement:", StringComparison.Ordinal))
                {
                    return text.Contains("Prorated", StringComparison.Ordinal)
                        ? ModelResponses.Done("passed", "The invoice is prorated.")
                        : ModelResponses.Done("failed", "No prorated amount.", "ASSERTION_FAILED");
                }

                onAct();
                if (text.Contains("tapped", StringComparison.Ordinal))
                {
                    return ModelResponses.Done("passed", "Upgraded to Pro.");
                }

                return ModelResponses.Tap("button", "Upgrade to Pro");
            }),
            Cache = new FileStepCache(directory),
            CacheEnabled = true,
            TestTitle = "billing > upgrades",
            Attempt = attempt,
        });
    }

    private static string TempCache() => Path.Combine(Path.GetTempPath(), "e2e-session", Guid.NewGuid().ToString("n"));
}
