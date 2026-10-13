// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json.Nodes;

namespace E2E;

/// <summary>
/// A project tool the agent offers to the model beside its own: upstream's <c>defineTool</c>. It declares whether it
/// changes the application and which platforms it is offered on; the agent owns dispatch and accounting. The tool's
/// name is its key in <see cref="AgentOptions.Tools"/>.
/// </summary>
public sealed class AgentTool
{
    /// <param name="description">What the model is told the tool does.</param>
    /// <param name="inputSchema">The JSON schema of the tool's input.</param>
    /// <param name="mutates">Whether running the tool can change the application. A mutating tool takes an action slot and ends replay of the step.</param>
    /// <param name="executeAsync">Runs the tool with its input as the model wrote it, and answers what the model reads.</param>
    /// <param name="platforms">The engine platforms the tool is offered on. Null means every platform.</param>
    public AgentTool(
        string description,
        JsonObject inputSchema,
        bool mutates,
        Func<JsonObject, CancellationToken, Task<string>> executeAsync,
        IReadOnlyList<string>? platforms = null)
    {
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(inputSchema);
        ArgumentNullException.ThrowIfNull(executeAsync);
        if (platforms is not null && (platforms.Count == 0 || platforms.Any(string.IsNullOrEmpty)))
        {
            throw new TestException("INVALID_ARGUMENT", "platforms must be a non-empty list of platform names when present");
        }

        Description = description;
        InputSchema = inputSchema;
        Mutates = mutates;
        ExecuteAsync = executeAsync;
        Platforms = platforms?.ToArray();
    }

    public string Description { get; }

    public JsonObject InputSchema { get; }

    public bool Mutates { get; }

    /// <summary>The platforms the tool is offered on. Null means every platform.</summary>
    public IReadOnlyList<string>? Platforms { get; }

    public Func<JsonObject, CancellationToken, Task<string>> ExecuteAsync { get; }

    /// <summary>True when the tool is offered on <paramref name="platform"/>.</summary>
    public bool AppliesTo(string platform) => Platforms is null || Platforms.Contains(platform, StringComparer.Ordinal);
}
