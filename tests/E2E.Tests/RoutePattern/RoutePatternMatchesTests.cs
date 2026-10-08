// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace E2E.Tests.RoutePattern;

public sealed class RoutePatternMatchesTests
{
    [Fact]
    public void Supports_regexp_wire_patterns()
    {
        Assert.True(E2E.Internal.Routes.Matcher(new Regex(@"api/\w+$"))("http://x.test/api/flags"));
    }
}
