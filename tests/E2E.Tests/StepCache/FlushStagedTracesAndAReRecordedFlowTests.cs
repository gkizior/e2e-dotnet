// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.StepCache;

/// <summary>"leaves an entry untouched when only the rule that flagged a typed value differs" is not ported: the port records no run-time value gaps.</summary>
public sealed class FlushStagedTracesAndAReRecordedFlowTests
{
    [Fact]
    public async Task Leaves_an_entry_the_same_flow_re_recorded_untouched_and_replaces_it_when_the_actions_change()
    {
        var directory = BillingSession.TempCache();
        await BillingSession.RecordAsync(directory);
        var file = Assert.Single(BillingSession.Entries(directory));
        var written = await File.ReadAllTextAsync(file);
        var stamp = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(file, stamp);

        // A retry runs live and records the same flow again: nothing a replay reads changed.
        await using (var session = await BillingSession.StartAsync(directory, () => { }, attempt: 2))
        {
            await BillingSession.UpgradeAsync(session);
            session.Complete();
        }

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(file));
        Assert.Equal(written, await File.ReadAllTextAsync(file));

        // One more tap is a different flow, and the entry follows it.
        await using (var session = await BillingSession.StartAsync(directory, TapsTwice(), attempt: 2))
        {
            await BillingSession.UpgradeAsync(session);
            session.Complete();
        }

        var replaced = await File.ReadAllTextAsync(Assert.Single(BillingSession.Entries(directory)));
        Assert.NotEqual(written, replaced);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Count(replaced, "\"kind\":\\s*\"tap\""));
    }

    /// <summary>A model that taps Upgrade to Pro twice before it concludes, and passes the judgment.</summary>
    private static ScriptedModel TapsTwice()
    {
        return new ScriptedModel(request =>
        {
            var text = string.Join('\n', request.Messages.Select(message => message.Content));
            if (text.Contains("Statement:", StringComparison.Ordinal))
            {
                return ModelResponses.Done("passed", "The invoice is prorated.");
            }

            var taps = request.Messages.Count(message => message.Role == "tool" && (message.Content ?? "").StartsWith("tapped", StringComparison.Ordinal));
            return taps >= 2 ? ModelResponses.Done("passed", "Upgraded to Pro.") : ModelResponses.Tap("button", "Upgrade to Pro");
        });
    }
}
