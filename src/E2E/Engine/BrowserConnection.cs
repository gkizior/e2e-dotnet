// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Playwright;

namespace E2E.Engine;

/// <summary>The browser connection, as upstream <c>browser-connection.ts</c>: every CDP attach goes through one bounded dial.</summary>
internal static class BrowserConnection
{
    /// <summary>
    /// Attaches to a Chromium over CDP, failing once <paramref name="timeout"/> passes
    /// without an answer instead of holding the caller.
    /// </summary>
    public static Task<IBrowser> ConnectCdpAsync(IPlaywright playwright, string endpoint, TimeSpan timeout, CancellationToken cancellationToken)
    {
        // Upstream registers its Playwright selector engines first; the port reads the page with its own script and registers none.
        cancellationToken.ThrowIfCancellationRequested();
        return playwright.Chromium.ConnectOverCDPAsync(endpoint, new BrowserTypeConnectOverCDPOptions { Timeout = (float)timeout.TotalMilliseconds });
    }
}
