// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.OAuth;

namespace E2E.Tests.Fetch;

/// <summary>
/// Device flows that poll without waiting out the interval, as upstream's do on fake timers. The login test
/// classes share this collection, so they run one at a time and the process-wide delay is put back after them.
/// </summary>
[CollectionDefinition(Name)]
public sealed class InstantDeviceFlow : ICollectionFixture<InstantDeviceFlow>, IDisposable
{
    public const string Name = "Instant device flow";

    private readonly Func<TimeSpan, CancellationToken, Task> _delay = DeviceFlow.Delay;

    public InstantDeviceFlow()
    {
        DeviceFlow.Delay = (_, _) => Task.CompletedTask;
    }

    public void Dispose() => DeviceFlow.Delay = _delay;
}
