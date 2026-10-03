// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Internal;

namespace E2E;

/// <summary>One turn of the agent model. Tool calls are executed by the agent, never by the model itself.</summary>
public interface IAgentModel
{
    /// <summary>Provider and model id, printed in the run summary. Changing it does not miss the replay cache.</summary>
    string Name { get; }

    Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken);
}

public sealed class ModelRequest
{
    public required string System { get; init; }

    public required IReadOnlyList<ModelMessage> Messages { get; init; }

    public required IReadOnlyList<ModelTool> Tools { get; init; }

    /// <summary>
    /// The agent's provider options, keyed by provider, for the model to pass on. Null when none are set.
    /// <see cref="OpenAiCompatibleModel"/> adds the entry under its provider name to the request body.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? ProviderOptions { get; init; }
}

public sealed class ModelMessage
{
    public required string Role { get; init; }

    public string? Content { get; init; }

    public IReadOnlyList<ModelToolCall>? ToolCalls { get; init; }

    public string? ToolCallId { get; init; }

    public string? Name { get; init; }
}

public sealed class ModelTool
{
    public required string Name { get; init; }

    public required string Description { get; init; }

    public required JsonElement Parameters { get; init; }
}

public sealed class ModelToolCall
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required JsonElement Arguments { get; init; }
}

public sealed class ModelUsage
{
    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }
}

public sealed class ModelResponse
{
    public string? Content { get; init; }

    public IReadOnlyList<ModelToolCall> ToolCalls { get; init; } = [];

    public ModelUsage? Usage { get; init; }
}

/// <summary>Builds the tool calls a <see cref="ScriptedModel"/> (or a test) returns.</summary>
public static class ModelResponses
{
    public static ModelResponse Done(string status, string summary, string? code = null)
    {
        return Call("done", code is null
            ? new { status, summary }
            : (object)new { status, summary, code });
    }

    public static ModelResponse Tap(string role, string name)
    {
        return Call("tap", new { role, name });
    }

    public static ModelResponse Fill(string role, string name, string value)
    {
        return Call("fill", new { role, name, value });
    }

    public static ModelResponse Call(string name, object arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(arguments);
        return new ModelResponse
        {
            ToolCalls =
            [
                new ModelToolCall
                {
                    Id = "call_" + name,
                    Name = name,
                    Arguments = JsonSerializer.SerializeToElement(arguments, JsonDefaults.Options),
                },
            ],
        };
    }
}

/// <summary>Returns whatever the callback decides for each request. Records every request.</summary>
public sealed class ScriptedModel : IAgentModel
{
    private readonly Func<ModelRequest, ModelResponse> _next;

    public ScriptedModel(Func<ModelRequest, ModelResponse> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        _next = next;
    }

    public string Name => "scripted";

    public int CallCount { get; private set; }

    public List<ModelRequest> Requests { get; } = [];

    public Task<ModelResponse> CompleteAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        Requests.Add(request);
        return Task.FromResult(_next(request));
    }
}
