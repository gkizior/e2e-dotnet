// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;
using static E2E.Tests.TraceRecorder.RecordedEntries;

namespace E2E.Tests.TraceRecorder;

/// <summary>"returns undefined for a node with nothing durable to say" is not ported: the port always records an action for a target.</summary>
public sealed class DescribeTargetTests
{
    /// <summary>Partial port of "drops text duplicating the name and never records values": the port records no text, and the value a field holds never reaches an action. An anchor may hold a value, which the port keeps.</summary>
    [Fact]
    public async Task Never_records_the_text_or_the_value_the_target_holds()
    {
        var directory = await RecordAsync(SettingsWorld(), null, ModelResponses.Call("navigate", new { url = "/billing" }), ModelResponses.Tap("button", "Upgrade"), ModelResponses.Tap("textbox", "Plan"));

        var actions = JsonNode.Parse(RawEntry(directory))!["actions"]!.ToJsonString();

        Assert.DoesNotContain("s3cr3t", actions, StringComparison.Ordinal);
        Assert.DoesNotContain("\"text\"", actions, StringComparison.Ordinal);
    }
}
