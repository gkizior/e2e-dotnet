// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ProtectedAppOptions;

/// <summary>The <c>TestIdAttribute</c> option is checked when the engine is built, so no element could carry a name that fails silently.</summary>
public sealed class WebTestIdAttributeTests
{
    [Theory]
    [InlineData("")]
    [InlineData("data qa")]
    [InlineData("data=\"qa\"")]
    public void Rejects_anything_that_is_not_an_attribute_name_as_INVALID_CONFIG(string attribute)
    {
        var error = Assert.Throws<EngineException>(() => new WebEngine(new WebEngineOptions { TestIdAttribute = attribute }));
        Assert.Equal("INVALID_CONFIG", error.Code);
        Assert.Matches("testIdAttribute.*must be an attribute name", error.Message);
    }
}
