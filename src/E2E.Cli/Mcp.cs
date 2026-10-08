// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;
using E2E.Cli.Mcp;
using E2E.Internal;

namespace E2E.Cli;

/// <summary>The flags of <c>e2e mcp</c>.</summary>
internal sealed class McpCommandOptions
{
    public string? Config { get; init; }

    public string? Target { get; init; }

    public bool Headed { get; init; }

    public int MaxSessions { get; init; } = SessionHost.SessionBounds.Default;
}

/// <summary>
/// The <c>e2e mcp</c> command: claims stdout for the protocol, then serves. Over stdio, stdout is the JSON-RPC
/// stream, so anything else that writes there would corrupt the handshake. The transport keeps the process's
/// own stdout stream, and every other write to <see cref="Console.Out"/> goes to stderr.
/// </summary>
internal static class McpCommand
{
    /// <summary>Runs the server until the client disconnects or a signal arrives; returns the exit code.</summary>
    public static async Task<int> RunAsync(McpCommandOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var protocol = Console.OpenStandardOutput();
        var stdin = Console.OpenStandardInput();
        var stderr = Console.Error;
        Console.SetOut(stderr);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var sigterm = PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            stop.Cancel();
        });
        try
        {
            return await Server.ServeAsync(
                new ServeOptions
                {
                    Cwd = Environment.CurrentDirectory,
                    ConfigPath = options.Config,
                    Target = options.Target,
                    Headed = options.Headed,
                    MaxSessions = options.MaxSessions,
                    Version = ModelHttp.PackageVersion(),
                    Stdin = stdin,
                    Stdout = protocol,
                    Log = line => stderr.WriteLine("e2e mcp: " + line),
                },
                stop.Token).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // An uncaught error is reported on stderr and ends the server with exit code 1.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            await stderr.WriteLineAsync("e2e mcp: [error] uncaught: " + Tools.CodedMessage(ex)).ConfigureAwait(false);
            return 1;
        }
    }
}
