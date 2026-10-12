// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Engine;
using static E2E.Mobile.Tests.RawNodes;

namespace E2E.Mobile.Tests.Selector;

public sealed class AgentDeviceSelectorsOverCapturedDeviceSnapshotsTests
{
    public static TheoryData<MobilePlatform> Platforms => [MobilePlatform.Ios, MobilePlatform.Android];

    /// <summary>A captured benchmark screen, projected the way the locator projects it.</summary>
    private static IReadOnlyList<ProjectedNode> Captured(MobilePlatform platform, string scenario)
    {
        var file = Path.Combine(AppContext.BaseDirectory, "Fixtures", "snapshots", platform.ToString().ToLowerInvariant(), $"{scenario}.json");
        var capture = JsonSerializer.Deserialize<Capture>(File.ReadAllText(file), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return Project(capture.Nodes).Index;
    }

    private static List<string?> Ids(MobilePlatform platform, string selector) =>
        [.. AgentDeviceSelector.Compile(selector, platform)(Captured(platform, "text-input-variations")).Select(entry => entry.Raw.Identifier)];

    [Theory]
    [MemberData(nameof(Platforms))]
    public void Matches_editable_by_agent_device_fillable_types(MobilePlatform platform)
    {
        Assert.Equal(["username-input", "pin-input", "notes-input"], Ids(platform, "editable"));
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void Matches_text_by_the_first_non_empty_label_value_or_identifier(MobilePlatform platform)
    {
        Assert.Equal(["username-input"], Ids(platform, "text=username-input"));
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void Matches_role_by_the_node_kind_not_the_getByRole_role(MobilePlatform platform)
    {
        Assert.Equal(
            platform == MobilePlatform.Ios ? ["username-input"] : ["username-input", "pin-input", "notes-input"],
            Ids(platform, "role=text-field"));
        Assert.Empty(Ids(platform, "role=textbox"));
    }

    [Theory]
    [MemberData(nameof(Platforms))]
    public void Returns_every_match_of_the_first_alternative_that_matches_in_snapshot_order(MobilePlatform platform)
    {
        Assert.Equal(["username-input", "pin-input", "notes-input"], Ids(platform, "id=missing || editable || role=button"));
        Assert.Empty(Ids(platform, "id=missing"));
    }

    [Fact]
    public void Rejects_a_ref_which_is_not_a_selector()
    {
        Assert.Equal("ENGINE_FAILURE", Assert.Throws<EngineException>(() => AgentDeviceSelector.Compile("@e3", MobilePlatform.Ios)).Code);
    }

    private sealed record Capture(RawNode[] Nodes);
}
