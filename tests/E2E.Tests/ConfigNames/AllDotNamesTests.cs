// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.ConfigNames;

public sealed class AllDotNamesTests
{
    [Fact]
    public void Refuses_a_target_named_dot_dot_as_INVALID_CONFIG_so_nothing_is_written_beside_report_json()
    {
        var error = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse(
            """{ "targets": [{ "name": "..", "app": { "url": "http://127.0.0.1:4599" } }] }""",
            Path.Combine(Path.GetTempPath(), "e2e-config-names"),
            _ => null));

        Assert.Equal("INVALID_CONFIG", error.Code);
        Assert.Contains(
            "targets[0].name must use ASCII letters, numbers, \"_\", \"-\", and \".\", and cannot be only dots",
            error.Message,
            StringComparison.Ordinal);
    }
}
