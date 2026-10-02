// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Internal;

namespace E2E;

/// <summary>
/// JSON config a host can load and apply on an NUnit fixture. There is no default
/// model: set <see cref="Agent"/> or run tests that do not call one.
/// </summary>
public sealed class E2EConfig
{
    public string Engine { get; set; } = "web";

    public AppConfig App { get; set; } = new();

    public AgentConfig? Agent { get; set; }

    public CacheConfig Cache { get; set; } = new();

    public TimeoutConfig Timeouts { get; set; } = new();

    public static E2EConfig Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<E2EConfig>(json, JsonDefaults.Options) ?? new E2EConfig();
    }
}

public sealed class AppConfig
{
    public string? Url { get; set; }
}

public sealed class AgentConfig
{
    public string? Model { get; set; }

    public string? BaseUrl { get; set; }

    public string? ApiKey { get; set; }

    public string ApiKeyEnv { get; set; } = "OPENAI_API_KEY";
}

public sealed class CacheConfig
{
    public string Directory { get; set; } = ".e2e/cache";

    public bool Enabled { get; set; } = true;
}

public sealed class TimeoutConfig
{
    public int TestMs { get; set; } = 60_000;

    public int ActionMs { get; set; } = 5_000;

    public int AssertionMs { get; set; } = 5_000;

    public int StepMs { get; set; } = 30_000;
}
