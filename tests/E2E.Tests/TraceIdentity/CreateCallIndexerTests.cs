// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.TraceIdentity;

public sealed class CreateCallIndexerTests
{
    [Fact]
    public void Counts_occurrences_per_signature_not_per_attempt()
    {
        var scope = new AttemptScope
        {
            Session = null!,
            Agents = new Dictionary<string, ResolvedAgent>(),
            Cache = null,
            CacheEnabled = true,
            TestTitle = "billing upgrade",
            EnginePlatform = "document",
        };
        var agent = new ResolvedAgent { Name = "default" };
        int Index(string instruction, IReadOnlyDictionary<string, object?>? parameters = null)
        {
            scope.ClaimKey(instruction, parameters, agent, out var callIndex);
            return callIndex;
        }

        Assert.Equal(0, Index("open billing"));
        Assert.Equal(0, Index("upgrade the plan"));
        Assert.Equal(1, Index("open billing"));
        Assert.Equal(0, Index("open billing", new Dictionary<string, object?> { ["fast"] = true }));
        Assert.Equal(1, Index("upgrade the plan"));
    }
}
