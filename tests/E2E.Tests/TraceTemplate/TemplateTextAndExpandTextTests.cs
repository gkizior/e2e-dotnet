// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.TraceTemplate;

/// <summary>
/// "refuses to expand a placeholder the current call did not mark" is not ported: the port leaves a slot the call did not
/// mark as it is, so a replay that cannot fill it misses on its own key.
/// </summary>
public sealed class TemplateTextAndExpandTextTests
{
    private static Dictionary<string, object?> Params(string name, string full) => new()
    {
        ["name"] = Values.Unique(name),
        ["full"] = Values.Unique(full),
    };

    [Fact]
    public void Round_trips_claims_the_longer_value_first_and_never_templates_inside_a_placeholder()
    {
        var recorded = Params("name", "Ada name");
        var templated = CacheKeys.Template("tap \"Ada name\" then \"name\" in the name column", recorded);

        Assert.Equal("tap \"\u0001full\u0001\" then \"\u0001name\u0001\" in the \u0001name\u0001 column", templated);
        Assert.Equal("tap \"Ada name\" then \"name\" in the name column", CacheKeys.Detemplate(templated, recorded));
        Assert.Equal(
            "tap \"Grace Hopper\" then \"Grace\" in the Grace column",
            CacheKeys.Detemplate(templated, Params("Grace", "Grace Hopper")));
    }
}
