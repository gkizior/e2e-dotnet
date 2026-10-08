// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Internal;

/// <summary>An engine with no open page reports <c>INVALID_STATE</c>; to the test that means no app is open.</summary>
internal static class AppNotOpen
{
    public static bool Is(EngineException ex) => ex.Code == EngineErrorCodes.InvalidState;

    public static TestException ForTest(EngineException ex) => new("APP_NOT_OPEN", ex.Message, ex);

    public static AgentException ForAgent(EngineException ex) => new("APP_NOT_OPEN", ex.Message, blocked: true, ex);

    /// <summary>Runs <paramref name="call"/> (a synchronous throw included), reporting an unopened app as <c>APP_NOT_OPEN</c>.</summary>
    public static async Task GuardAsync(Func<Task> call)
    {
        try
        {
            await call().ConfigureAwait(false);
        }
        catch (EngineException ex) when (Is(ex))
        {
            throw ForTest(ex);
        }
    }

    /// <inheritdoc cref="GuardAsync(Func{Task})"/>
    public static async Task<T> GuardAsync<T>(Func<Task<T>> call)
    {
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (EngineException ex) when (Is(ex))
        {
            throw ForTest(ex);
        }
    }
}
