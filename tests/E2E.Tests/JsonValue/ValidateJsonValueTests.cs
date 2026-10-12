// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Internal;

namespace E2E.Tests.JsonValue;

/// <summary>
/// The port checks the params of <c>agent.act</c> in <c>ActParams.Validate</c>, with a fixed depth limit of 32 and no
/// JavaScript prototypes. So the upstream <c>validateJsonValue</c> its are ported as far as C# can write them.
/// </summary>
public sealed class ValidateJsonValueTests
{
    [Fact]
    public void Accepts_the_same_object_reached_twice_by_different_paths()
    {
        var shared = new Dictionary<string, object?> { ["a"] = 1 };
        var parameters = new Dictionary<string, object?>
        {
            ["p"] = shared,
            ["q"] = shared,
            ["r"] = new List<object?> { shared, shared },
        };

        ActParams.Validate(parameters);
    }

    [Fact]
    public void Rejects_an_object_that_contains_itself()
    {
        var self = new Dictionary<string, object?>();
        self["me"] = self;
        var error = Assert.Throws<TestException>(() => ActParams.Validate(self));
        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.Contains("contains a cycle", error.Message);

        var list = new List<object?>();
        list.Add(new Dictionary<string, object?> { ["list"] = list });
        var inList = Assert.Throws<TestException>(() => ActParams.Validate(new Dictionary<string, object?> { ["items"] = list }));
        Assert.Contains("contains a cycle", inList.Message);
    }

    /// <summary>Partial: C# has no <c>Map</c> and no prototype-less object, so only the non-finite number half can be written.</summary>
    [Fact]
    public void Rejects_a_non_finite_number()
    {
        var error = Assert.Throws<TestException>(() => ActParams.Validate(new Dictionary<string, object?> { ["n"] = double.NaN }));
        Assert.Equal("INVALID_ARGUMENT", error.Code);
        Assert.Contains("non-finite", error.Message);
    }

    /// <summary>Partial: the port fixes the limit at 32 levels, where upstream takes a <c>maxDepth</c> option.</summary>
    [Fact]
    public void Enforces_the_depth_limit_of_32_levels()
    {
        static Dictionary<string, object?> Nested(int levels)
        {
            var innermost = new Dictionary<string, object?> { ["leaf"] = 1 };
            for (var level = 1; level < levels; level++)
            {
                innermost = new Dictionary<string, object?> { ["a"] = innermost };
            }

            return innermost;
        }

        var error = Assert.Throws<TestException>(() => ActParams.Validate(Nested(ActParams.MaxDepth + 2)));
        Assert.Contains("levels", error.Message);
        ActParams.Validate(Nested(ActParams.MaxDepth));
    }
}
