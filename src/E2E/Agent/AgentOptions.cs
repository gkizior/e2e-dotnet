// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text;
using System.Text.Json;

namespace E2E;

/// <summary>
/// One named agent, as upstream <c>agents.&lt;name&gt;</c>. Each entry starts from the built-in
/// defaults; no agent inherits another's values. A call picks one with its <c>Agent</c> option.
/// </summary>
public sealed class AgentOptions
{
    /// <summary>The model behind <c>act</c>. There is no implicit default.</summary>
    public IAgentModel? Model { get; init; }

    /// <summary>
    /// The model that judges <c>assert</c>, <c>waitFor</c>, and <c>extract</c>. Defaults to
    /// <see cref="Model"/>. A judge of its own separates the model that grades a flow from the one that drove it.
    /// </summary>
    public IAgentModel? Judge { get; init; }

    /// <summary>
    /// How the acting agent should work, appended to the built-in execution rules. Only the act loop
    /// reads it; the judges never see it.
    /// </summary>
    public string? System { get; init; }

    /// <summary>
    /// What the app calls things, told to every model call this agent makes, act turns and judgments
    /// alike. At most 16384 UTF-8 bytes.
    /// </summary>
    public string? Context { get; init; }

    /// <summary>Committed actions per <c>act</c>, 1 through 100.</summary>
    public int MaxSteps { get; init; } = E2EDefaults.MaxSteps;

    /// <summary>Model requests per agent call, 1 through 100.</summary>
    public int MaxModelCalls { get; init; } = E2EDefaults.MaxModelCalls;

    /// <summary>Deadline of one <c>assert</c>, <c>waitFor</c>, or <c>extract</c>.</summary>
    public TimeSpan JudgmentTimeout { get; init; } = E2EDefaults.JudgmentTimeout;

    /// <summary>
    /// Provider options every model call carries, keyed by provider. <see cref="OpenAiCompatibleModel"/>
    /// adds the fields under its <see cref="OpenAiCompatibleModelOptions.Provider"/> key to the request body as given.
    /// </summary>
    public IReadOnlyDictionary<string, JsonElement>? ProviderOptions { get; init; }
}

/// <summary>An agent with its settings checked and its judge resolved.</summary>
internal sealed class ResolvedAgent
{
    /// <summary>The most bytes <see cref="AgentOptions.Context"/> may hold.</summary>
    public const int MaxContextBytes = 16_384;

    /// <summary>The largest <c>maxSteps</c> and <c>maxModelCalls</c> an agent may configure.</summary>
    public const int MaxBudget = 100;

    public required string Name { get; init; }

    public IAgentModel? Model { get; init; }

    public IAgentModel? Judge { get; init; }

    public string? System { get; init; }

    public string? Context { get; init; }

    public int MaxSteps { get; init; } = E2EDefaults.MaxSteps;

    public int MaxModelCalls { get; init; } = E2EDefaults.MaxModelCalls;

    public TimeSpan JudgmentTimeout { get; init; } = E2EDefaults.JudgmentTimeout;

    public IReadOnlyDictionary<string, JsonElement>? ProviderOptions { get; init; }

    /// <summary>Checks one agent's settings. Throws <see cref="ConfigurationException"/> with <c>INVALID_CONFIG</c>.</summary>
    public static ResolvedAgent From(string name, AgentOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var label = "agents." + name;
        Check(options.MaxSteps, label + ".maxSteps");
        Check(options.MaxModelCalls, label + ".maxModelCalls");
        if (options.JudgmentTimeout <= TimeSpan.Zero)
        {
            throw new ConfigurationException("INVALID_CONFIG", label + ".judgmentTimeout must be a positive integer of milliseconds");
        }

        CheckContext(options.Context, label + ".context");
        CheckProviderOptions(options.ProviderOptions, label);
        return new ResolvedAgent
        {
            Name = name,
            Model = options.Model,
            Judge = options.Judge ?? options.Model,
            System = options.System,
            Context = options.Context,
            MaxSteps = options.MaxSteps,
            MaxModelCalls = options.MaxModelCalls,
            JudgmentTimeout = options.JudgmentTimeout,
            ProviderOptions = options.ProviderOptions,
        };
    }

    internal static void Check(int value, string label)
    {
        if (value is < 1 or > MaxBudget)
        {
            throw new ConfigurationException(
                "INVALID_CONFIG",
                label + " must be an integer from 1 to " + MaxBudget.ToString(CultureInfo.InvariantCulture));
        }
    }

    internal static void CheckContext(string? context, string label)
    {
        if (context is null)
        {
            return;
        }

        var bytes = Encoding.UTF8.GetByteCount(context);
        if (bytes > MaxContextBytes)
        {
            throw new ConfigurationException(
                "INVALID_CONFIG",
                label + " is " + bytes.ToString(CultureInfo.InvariantCulture) + " bytes; the maximum is " + MaxContextBytes.ToString(CultureInfo.InvariantCulture));
        }
    }

    internal static void CheckProviderOptions(IReadOnlyDictionary<string, JsonElement>? providerOptions, string label)
    {
        if (providerOptions is null)
        {
            return;
        }

        foreach (var pair in providerOptions)
        {
            if (pair.Value.ValueKind != JsonValueKind.Object)
            {
                throw new ConfigurationException("INVALID_CONFIG", label + ".providerOptions." + pair.Key + " must be an object of provider options");
            }
        }
    }
}
