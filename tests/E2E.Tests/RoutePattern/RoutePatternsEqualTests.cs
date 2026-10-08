// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace E2E.Tests.RoutePattern;

public sealed class RoutePatternsEqualTests
{
    // .NET has no global flag, so Multiline stands in for upstream's /a/g: another option.
    [Fact]
    public void Compares_string_and_regexp_forms_structurally()
    {
        Assert.True(E2E.Internal.Routes.PatternsEqual("**/a", "**/a"));
        Assert.True(E2E.Internal.Routes.PatternsEqual(new Regex("a", RegexOptions.IgnoreCase), new Regex("a", RegexOptions.IgnoreCase)));
        Assert.False(E2E.Internal.Routes.PatternsEqual(new Regex("a", RegexOptions.IgnoreCase), new Regex("a", RegexOptions.Multiline)));
        Assert.False(E2E.Internal.Routes.PatternsEqual("a", new Regex("a")));
    }
}
