// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.Config;

public sealed class CiModeTests
{
    [Fact]
    public void Is_active_unless_CI_is_empty_0_or_false_case_insensitive()
    {
        Assert.False(E2EConfig.IsCi(Env(null)));
        Assert.False(E2EConfig.IsCi(Env("")));
        Assert.False(E2EConfig.IsCi(Env("0")));
        Assert.False(E2EConfig.IsCi(Env("False")));
        Assert.True(E2EConfig.IsCi(Env("true")));
        Assert.True(E2EConfig.IsCi(Env("1")));
    }

    private static Func<string, string?> Env(string? ci) => name => name == "CI" ? ci : null;
}
