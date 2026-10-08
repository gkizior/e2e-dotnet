// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.Store;

/// <summary>
/// Upstream hands the store an environment object; the port reads the process environment. Tests that set a
/// variable run in this collection, one at a time, and put the old value back.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessEnvironment
{
    public const string Name = "Process environment";

    public static IDisposable Set(string name, string? value)
    {
        var old = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        return new Restore(name, old);
    }

    private sealed class Restore(string name, string? old) : IDisposable
    {
        public void Dispose() => Environment.SetEnvironmentVariable(name, old);
    }
}
