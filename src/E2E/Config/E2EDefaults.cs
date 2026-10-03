// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E;

/// <summary>
/// The upstream defaults, in one place. <see cref="E2EConfig"/>, <see cref="E2ESessionOptions"/>,
/// and the NUnit fixture all start from these values.
/// </summary>
public static class E2EDefaults
{
    /// <summary>Upstream <c>timeout</c>: the whole test.</summary>
    public static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(120);

    /// <summary>Upstream <c>launchTimeout</c>.</summary>
    public static readonly TimeSpan LaunchTimeout = TimeSpan.FromSeconds(60);

    /// <summary>Upstream <c>actionTimeout</c>.</summary>
    public static readonly TimeSpan ActionTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Upstream <c>assertionTimeout</c>.</summary>
    public static readonly TimeSpan AssertionTimeout = TimeSpan.FromSeconds(5);

    /// <summary>Upstream <c>cleanupTimeout</c>.</summary>
    public static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long one <c>act</c> may run. A .NET-only setting; upstream bounds <c>act</c> by the test timeout.</summary>
    public static readonly TimeSpan StepTimeout = TimeSpan.FromSeconds(30);

    /// <summary>Upstream <c>judgmentTimeout</c>: the deadline of one <c>assert</c>, <c>waitFor</c>, or <c>extract</c>.</summary>
    public static readonly TimeSpan JudgmentTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long a replay waits for each recorded target and for the recorded end state.</summary>
    public static readonly TimeSpan ReplayTimeout = TimeSpan.FromSeconds(15);

    /// <summary>How often <c>agent.waitFor</c> looks again.</summary>
    public static readonly TimeSpan WaitForInterval = TimeSpan.FromSeconds(3);

    /// <summary>Upstream <c>maxModelCalls</c> for one agent call.</summary>
    public const int MaxModelCalls = 25;

    /// <summary>Upstream <c>maxSteps</c>: actions one <c>act</c> may take.</summary>
    public const int MaxSteps = 25;
}
