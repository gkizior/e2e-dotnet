// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Mobile;

/// <summary>Best-effort cleanup: abandon the wait at cancellation or timeout, absorbing late failures.</summary>
internal static class CleanupBudget
{
    /// <summary>Runs the work under a token that is cancelled when the budget or the caller's token runs out, and waits no longer than that.</summary>
    public static async Task WithinAsync(Func<CancellationToken, Task> work, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var budget = timeout < TimeSpan.Zero ? TimeSpan.Zero : timeout;
        using var expiry = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        expiry.CancelAfter(budget);
        var expired = new TaskCompletionSource();
        await using var registration = expiry.Token.Register(() => expired.TrySetResult());
        var finished = work(expiry.Token).ContinueWith(static _ => { }, TaskScheduler.Default);
        await Task.WhenAny(finished, expired.Task).ConfigureAwait(false);
    }
}

/// <summary>The agent-device this package pins exactly: the version every client speaks.</summary>
public static class AgentDeviceVersion
{
    /// <summary>The agent-device release the wire format here was read from.</summary>
    public const string Pinned = "0.21.22";

    /// <summary>The daemon's JSON-RPC protocol version the client speaks, as <c>GET /health</c> reports it.</summary>
    public const int RpcProtocol = 2;

    /// <summary>The oldest Node the agent-device daemon runs on.</summary>
    public static readonly Version MinimumNode = new(22, 12);
}
