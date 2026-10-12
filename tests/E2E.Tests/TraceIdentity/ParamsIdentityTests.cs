// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.TraceIdentity;

public sealed class ParamsIdentityTests
{
    [Fact]
    public void Digests_missing_params_as_the_empty_object_independent_of_key_order()
    {
        Assert.Equal(CacheKeys.ParamsDigest(null), CacheKeys.ParamsDigest(new Dictionary<string, object?>()));
        Assert.Equal(
            CacheKeys.ParamsDigest(new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2 }),
            CacheKeys.ParamsDigest(new Dictionary<string, object?> { ["b"] = 2, ["a"] = 1 }));
        Assert.NotEqual(
            CacheKeys.ParamsDigest(new Dictionary<string, object?> { ["a"] = 1 }),
            CacheKeys.ParamsDigest(new Dictionary<string, object?> { ["a"] = 2 }));
    }
}
