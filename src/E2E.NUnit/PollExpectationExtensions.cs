// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using NUnit.Framework.Constraints;

namespace E2E.NUnit;

/// <summary>NUnit constraints for <c>Expect.Poll</c>, in place of the upstream value matchers.</summary>
public static class PollExpectationExtensions
{
    /// <summary>
    /// Re-reads the value until <paramref name="constraint"/> holds, such as
    /// <c>Is.GreaterThan(3)</c> or <c>Has.Count.EqualTo(2)</c>.
    /// </summary>
    public static Task ToMatchAsync<T>(this PollExpectation<T> poll, IResolveConstraint constraint, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(poll);
        ArgumentNullException.ThrowIfNull(constraint);
        var resolved = constraint.Resolve();
        return poll.ToSatisfyAsync(value => resolved.ApplyTo(value).IsSuccess, resolved.Description, cancellationToken);
    }
}
