// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;

namespace E2E.Tests.EngineError;

/// <summary>
/// The engine error contract: closed codes and closed retryability. Upstream's <c>asEngineError</c> describe is not
/// ported: it recognizes an error thrown by another copy of a JavaScript module, which .NET has no counterpart for.
/// </summary>
public sealed class EngineErrorRetryabilityTests
{
    private static readonly string[] Codes = typeof(EngineErrorCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(field => field.IsLiteral && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToArray();

    [Fact]
    public void Keeps_a_legal_retryable_claim()
    {
        var legal = Codes.Where(EngineErrorCodes.IsRetryable).ToArray();
        Assert.Equal(["NODE_STALE", "FRAME_NOT_FOUND"], legal.Order(StringComparer.Ordinal).Reverse());
        foreach (var code in legal)
        {
            var error = new EngineException(code, "again", retryable: true);
            Assert.Equal(code, error.Code);
            Assert.True(error.Retryable);
        }
    }

    [Fact]
    public void Coerces_an_illegal_retryable_claim_to_a_non_retryable_ENGINE_FAILURE()
    {
        var illegal = Codes.Where(code => !EngineErrorCodes.IsRetryable(code)).ToArray();
        Assert.Contains("ACTION_MAY_HAVE_COMMITTED", illegal);
        foreach (var code in illegal)
        {
            var error = new EngineException(code, "maybe committed", retryable: true);
            Assert.Equal("ENGINE_FAILURE", error.Code);
            Assert.False(error.Retryable);
        }
    }

    [Fact]
    public void Never_rewrites_a_non_retryable_error()
    {
        var error = new EngineException("ACTION_MAY_HAVE_COMMITTED", "sent", retryable: false);

        Assert.Equal("ACTION_MAY_HAVE_COMMITTED", error.Code);
        Assert.False(new EngineException("NODE_STALE", "gone").Retryable);
    }
}
