// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;

namespace E2E;

/// <summary>
/// The resolved <c>e2e.config.json</c>. The JSON shape follows upstream <c>e2e.config.ts</c>:
/// <c>targets[].app.url</c>, named <c>agents</c>, top-level timeouts in milliseconds,
/// <c>retries</c>, <c>cache</c>, and <c>secrets</c>. Unknown keys fail with
/// <c>INVALID_CONFIG</c>. <c>E2ETest</c> finds and applies this file; a fixture can
/// still override each value. There is no default model.
/// </summary>
public sealed class E2EConfig
{
    /// <summary>The file name <see cref="Find"/> looks for.</summary>
    public const string FileName = "e2e.config.json";

    private static readonly Dictionary<string, string> MovedKeys = new(StringComparer.Ordinal)
    {
        ["app"] = "the app under test is declared on its target: targets: [{ \"app\": { \"url\": ... } }]",
        ["engine"] = "the engine is chosen by the fixture; declare the target as targets: [{ \"platform\": \"web\" }]",
        ["agent"] = "agents are named: agents: { \"default\": { \"model\": ... } }",
        ["timeouts"] = "timeouts are top-level milliseconds: timeout, launchTimeout, actionTimeout, assertionTimeout, cleanupTimeout",
    };

    private static readonly string[] TopLevelKeys =
    [
        "targets", "timeout", "launchTimeout", "actionTimeout", "assertionTimeout", "cleanupTimeout",
        "retries", "agents", "cache", "secrets",
    ];

    // Upstream keys the .NET port does not implement. They fail loudly rather than being ignored.
    private static readonly string[] UnsupportedTopLevelKeys =
    [
        "projectId", "tests", "failOnSkippedFailure", "workers", "artifacts", "output", "trace", "video", "reporters", "credentials",
    ];

    private static readonly string[] TargetKeys = ["name", "platform", "app"];

    private static readonly string[] UnsupportedTargetKeys = ["engine", "trace", "video"];

    private static readonly string[] AppKeys = ["url"];

    private static readonly string[] UnsupportedAppKeys =
        ["bundleId", "appPath", "identity", "environment", "launchArguments", "permissions", "command", "readyUrl"];

    private static readonly string[] AgentKeys =
        ["model", "judge", "provider", "system", "context", "baseUrl", "apiKeyEnv", "maxSteps", "maxModelCalls", "judgmentTimeout", "providerOptions"];

    private static readonly string[] UnsupportedAgentKeys = ["tools", "executor", "maxObservationBytes", "maxInputTokens"];

    private static readonly string[] CacheKeys = ["mode", "dir", "strict"];

    private static readonly string[] UnsupportedCacheKeys = ["store"];

    private E2EConfig()
    {
    }

    /// <summary>The file this config was read from, or null for the defaults.</summary>
    public string? ConfigPath { get; private init; }

    /// <summary>The config file's directory. Relative paths such as <c>cache.dir</c> resolve against it.</summary>
    public string ProjectRoot { get; private init; } = "";

    /// <summary>True when the <c>CI</c> environment variable is set to a true value.</summary>
    public bool Ci { get; private init; }

    public IReadOnlyList<TargetConfig> Targets { get; private init; } = [];

    /// <summary>The target a test runs on. The port runs one target.</summary>
    public TargetConfig Target => Targets[0];

    /// <summary>Whole-test timeout. Upstream <c>timeout</c>, 120 s by default.</summary>
    public TimeSpan Timeout { get; private init; } = E2EDefaults.TestTimeout;

    /// <summary>Engine start timeout. Upstream <c>launchTimeout</c>, 60 s by default.</summary>
    public TimeSpan LaunchTimeout { get; private init; } = E2EDefaults.LaunchTimeout;

    public TimeSpan ActionTimeout { get; private init; } = E2EDefaults.ActionTimeout;

    public TimeSpan AssertionTimeout { get; private init; } = E2EDefaults.AssertionTimeout;

    /// <summary>Engine shutdown timeout. Upstream <c>cleanupTimeout</c>, 30 s by default.</summary>
    public TimeSpan CleanupTimeout { get; private init; } = E2EDefaults.CleanupTimeout;

    /// <summary>0 to 10. Defaults to 1 in CI and 0 elsewhere. NUnit retries still come from <c>[Retry]</c>.</summary>
    public int Retries { get; private init; }

    public IReadOnlyDictionary<string, AgentConfig> Agents { get; private init; } = ReadOnlyDictionary<string, AgentConfig>.Empty;

    /// <summary>The <c>default</c> agent. It always exists; its model may be null.</summary>
    public AgentConfig Agent => Agents["default"];

    public CacheConfig Cache { get; private init; } = new();

    public Secrets Secrets { get; private init; } = Secrets.Empty;

    /// <summary>Reads and validates a config file. Throws <see cref="ConfigurationException"/> with <c>INVALID_CONFIG</c>.</summary>
    public static E2EConfig Load(string path, Func<string, string?>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        string json;
        try
        {
            json = File.ReadAllText(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new ConfigurationException("INVALID_CONFIG", "Cannot read " + full + ": " + ex.Message, ex);
        }

        return Parse(json, Path.GetDirectoryName(full)!, full, environment);
    }

    /// <summary>Validates config JSON. <paramref name="projectRoot"/> anchors relative paths.</summary>
    public static E2EConfig Parse(string json, string projectRoot, Func<string, string?>? environment = null)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        return Parse(json, Path.GetFullPath(projectRoot), null, environment);
    }

    /// <summary>The config with no file: upstream defaults, a <c>web</c> target with no URL, and no model.</summary>
    public static E2EConfig Default(string projectRoot, Func<string, string?>? environment = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);
        return Parse("{}", Path.GetFullPath(projectRoot), null, environment);
    }

    /// <summary>
    /// The nearest <see cref="FileName"/> in each start directory or one of its parents,
    /// tried in order, or null.
    /// </summary>
    public static string? Find(params string[] startDirectories)
    {
        ArgumentNullException.ThrowIfNull(startDirectories);
        foreach (var start in startDirectories)
        {
            if (string.IsNullOrWhiteSpace(start))
            {
                continue;
            }

            for (var directory = new DirectoryInfo(Path.GetFullPath(start)); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, FileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Loads the nearest <see cref="FileName"/> above the test assembly directory, then
    /// above the working directory. Without one, returns <see cref="Default"/> for the working directory.
    /// </summary>
    public static E2EConfig Discover(Func<string, string?>? environment = null)
    {
        var path = Find(AppContext.BaseDirectory, Directory.GetCurrentDirectory());
        return path is null ? Default(Directory.GetCurrentDirectory(), environment) : Load(path, environment);
    }

    /// <summary>True when <c>CI</c> is set to anything but empty, <c>0</c>, or <c>false</c>.</summary>
    public static bool IsCi(Func<string, string?>? environment = null)
    {
        var value = (environment ?? Environment.GetEnvironmentVariable)("CI")?.Trim();
        return !string.IsNullOrEmpty(value)
            && value != "0"
            && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    }

    private static E2EConfig Parse(string json, string projectRoot, string? configPath, Func<string, string?>? environment)
    {
        environment ??= Environment.GetEnvironmentVariable;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            throw Invalid((configPath ?? "config") + " is not valid JSON: " + ex.Message);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("the config must be a JSON object");
            }

            CheckKeys(root, "config", TopLevelKeys, UnsupportedTopLevelKeys, MovedKeys);
            var ci = IsCi(environment);
            return new E2EConfig
            {
                ConfigPath = configPath,
                ProjectRoot = projectRoot,
                Ci = ci,
                Targets = ReadTargets(Property(root, "targets")),
                Timeout = Milliseconds(root, "timeout", E2EDefaults.TestTimeout),
                LaunchTimeout = Milliseconds(root, "launchTimeout", E2EDefaults.LaunchTimeout),
                ActionTimeout = Milliseconds(root, "actionTimeout", E2EDefaults.ActionTimeout),
                AssertionTimeout = Milliseconds(root, "assertionTimeout", E2EDefaults.AssertionTimeout),
                CleanupTimeout = Milliseconds(root, "cleanupTimeout", E2EDefaults.CleanupTimeout),
                Retries = Property(root, "retries") is { } retries ? Integer(retries, "retries", 0, 10) : ci ? 1 : 0,
                Agents = ReadAgents(Property(root, "agents")),
                Cache = ReadCache(Property(root, "cache"), projectRoot, ci),
                Secrets = ReadSecrets(Property(root, "secrets"), environment),
            };
        }
    }

    private static List<TargetConfig> ReadTargets(JsonElement? raw)
    {
        if (raw is null)
        {
            return [new TargetConfig { Name = "web", Platform = "web", App = new AppConfig() }];
        }

        if (raw.Value.ValueKind != JsonValueKind.Array || raw.Value.GetArrayLength() == 0)
        {
            throw Invalid("targets must be a non-empty array");
        }

        if (raw.Value.GetArrayLength() > 1)
        {
            throw Invalid("targets has " + raw.Value.GetArrayLength().ToString(CultureInfo.InvariantCulture) + " entries; the .NET port runs one target");
        }

        var target = raw.Value[0];
        if (target.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("targets[0] must be an object");
        }

        CheckKeys(target, "targets[0]", TargetKeys, UnsupportedTargetKeys, null);
        var platform = OptionalString(target, "platform", "targets[0].platform") ?? "web";
        if (!string.Equals(platform, "web", StringComparison.Ordinal))
        {
            throw Invalid("targets[0].platform \"" + platform + "\" is not supported; the .NET port drives \"web\"");
        }

        var name = OptionalString(target, "name", "targets[0].name") ?? platform;
        if (!ValidName(name))
        {
            throw Invalid("targets[0].name must use ASCII letters, numbers, \"_\", \"-\", and \".\", and cannot be only dots");
        }

        var app = new AppConfig();
        if (Property(target, "app") is { } rawApp)
        {
            if (rawApp.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("targets[0].app must be an object");
            }

            CheckKeys(rawApp, "targets[0].app", AppKeys, UnsupportedAppKeys, null);
            app = new AppConfig { Url = OptionalString(rawApp, "url", "targets[0].app.url") };
        }

        return [new TargetConfig { Name = name, Platform = platform, App = app }];
    }

    private static ReadOnlyDictionary<string, AgentConfig> ReadAgents(JsonElement? raw)
    {
        var agents = new Dictionary<string, AgentConfig>(StringComparer.Ordinal);
        if (raw is not null)
        {
            if (raw.Value.ValueKind != JsonValueKind.Object)
            {
                throw Invalid("agents must be an object of agents by name: agents: { \"default\": { \"model\": ... } }");
            }

            foreach (var property in raw.Value.EnumerateObject())
            {
                if (!ValidName(property.Name))
                {
                    throw Invalid("agent name \"" + property.Name + "\" must use ASCII letters, numbers, \"_\", \"-\", and \".\", and cannot be only dots");
                }

                var label = "agents." + property.Name;
                var value = property.Value;
                if (value.ValueKind != JsonValueKind.Object)
                {
                    throw Invalid(label + " must be an object");
                }

                CheckKeys(value, label, AgentKeys, UnsupportedAgentKeys, null);
                var context = AnyString(value, "context", label + ".context");
                ResolvedAgent.CheckContext(context, label + ".context");
                var provider = OptionalString(value, "provider", label + ".provider") ?? AgentConfig.DefaultProvider;
                if (!AgentConfig.Providers.Contains(provider, StringComparer.Ordinal))
                {
                    throw Invalid(label + ".provider must be one of " + string.Join(", ", AgentConfig.Providers.Select(name => "\"" + name + "\"")));
                }

                var baseUrl = OptionalString(value, "baseUrl", label + ".baseUrl");
                var apiKeyEnv = OptionalString(value, "apiKeyEnv", label + ".apiKeyEnv");
                if (AgentConfig.IsSubscription(provider) && (baseUrl is not null || apiKeyEnv is not null))
                {
                    throw Invalid(label + ": the " + provider + " subscription reads its stored login, so baseUrl and apiKeyEnv do not apply");
                }

                if (provider == "openai-compatible" && baseUrl is null)
                {
                    throw Invalid(label + ".baseUrl is required for provider \"openai-compatible\"");
                }

                agents[property.Name] = new AgentConfig
                {
                    Model = OptionalString(value, "model", label + ".model"),
                    Judge = OptionalString(value, "judge", label + ".judge"),
                    Provider = provider,
                    System = AnyString(value, "system", label + ".system"),
                    Context = context,
                    BaseUrl = baseUrl,
                    ApiKeyEnv = apiKeyEnv,
                    MaxSteps = Property(value, "maxSteps") is { } steps ? Integer(steps, label + ".maxSteps", 1, ResolvedAgent.MaxBudget) : E2EDefaults.MaxSteps,
                    MaxModelCalls = Property(value, "maxModelCalls") is { } calls ? Integer(calls, label + ".maxModelCalls", 1, ResolvedAgent.MaxBudget) : E2EDefaults.MaxModelCalls,
                    JudgmentTimeout = Property(value, "judgmentTimeout") is { } judgment
                        ? TimeSpan.FromMilliseconds(Integer(judgment, label + ".judgmentTimeout", 1, int.MaxValue))
                        : E2EDefaults.JudgmentTimeout,
                    ProviderOptions = ReadProviderOptions(Property(value, "providerOptions"), label),
                };
            }
        }

        agents.TryAdd("default", new AgentConfig());
        return new ReadOnlyDictionary<string, AgentConfig>(agents);
    }

    private static ReadOnlyDictionary<string, JsonElement>? ReadProviderOptions(JsonElement? raw, string label)
    {
        if (raw is null)
        {
            return null;
        }

        if (raw.Value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid(label + ".providerOptions must be an object of provider options by provider");
        }

        var options = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var provider in raw.Value.EnumerateObject())
        {
            if (provider.Value.ValueKind != JsonValueKind.Object)
            {
                throw Invalid(label + ".providerOptions." + provider.Name + " must be an object of provider options");
            }

            options[provider.Name] = provider.Value.Clone();
        }

        return new ReadOnlyDictionary<string, JsonElement>(options);
    }

    private static CacheConfig ReadCache(JsonElement? raw, string projectRoot, bool ci)
    {
        var defaultDirectory = Path.Combine(projectRoot, ".e2e", "cache");
        if (raw is null)
        {
            return new CacheConfig { Mode = ci ? CacheMode.ReadOnly : CacheMode.ReadWrite, Directory = defaultDirectory };
        }

        if (raw.Value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("cache must be an object");
        }

        var value = raw.Value;
        CheckKeys(value, "cache", CacheKeys, UnsupportedCacheKeys, null);
        CacheMode mode;
        if (Property(value, "mode") is { } rawMode)
        {
            mode = (rawMode.ValueKind == JsonValueKind.String ? rawMode.GetString() : null) switch
            {
                "off" => CacheMode.Off,
                "read-only" => CacheMode.ReadOnly,
                "read-write" => CacheMode.ReadWrite,
                _ => throw Invalid("cache.mode must be \"off\", \"read-only\", or \"read-write\""),
            };
        }
        else
        {
            // An unset mode is read-write locally. CI treats recordings as untrusted input and only reads them.
            mode = ci ? CacheMode.ReadOnly : CacheMode.ReadWrite;
        }

        var directory = defaultDirectory;
        if (Property(value, "dir") is { } rawDir)
        {
            var dir = rawDir.ValueKind == JsonValueKind.String ? rawDir.GetString() : null;
            if (string.IsNullOrWhiteSpace(dir))
            {
                throw Invalid("cache.dir must be a non-empty path");
            }

            directory = Path.GetFullPath(Path.Combine(projectRoot, dir));
        }

        var strict = false;
        if (Property(value, "strict") is { } rawStrict)
        {
            strict = rawStrict.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => throw Invalid("cache.strict must be a boolean"),
            };
        }

        return new CacheConfig { Mode = mode, Directory = directory, Strict = strict };
    }

    private static Secrets ReadSecrets(JsonElement? raw, Func<string, string?> environment)
    {
        if (raw is null)
        {
            return Secrets.Empty;
        }

        if (raw.Value.ValueKind != JsonValueKind.Object)
        {
            throw Invalid("secrets must be an object of secret values by name");
        }

        var variables = new Dictionary<string, string>(StringComparer.Ordinal);
        var values = new Dictionary<string, Secret>(StringComparer.Ordinal);
        foreach (var property in raw.Value.EnumerateObject())
        {
            var name = property.Name;
            if (name.Trim().Length == 0)
            {
                throw Invalid("secret names must be non-empty");
            }

            var variable = Secrets.EnvironmentVariable(name);
            if (variables.TryGetValue(variable, out var other))
            {
                throw Invalid(
                    "secrets \"" + other + "\" and \"" + name + "\" names share override variables, so one value set there would replace all of them (" + variable + ")");
            }

            variables[variable] = name;
            string? entry = property.Value.ValueKind switch
            {
                JsonValueKind.String => property.Value.GetString(),
                JsonValueKind.Null => null,
                _ => throw Invalid("secret \"" + name + "\" must be a non-empty string, or null to read only " + variable),
            };
            var value = environment(variable) ?? entry;
            if (value is null)
            {
                throw Invalid("secret \"" + name + "\" has no value; set " + variable);
            }

            if (Secret.Problem(value) is { } problem)
            {
                throw Invalid("secret \"" + name + "\" " + problem);
            }

            values[name] = Secret.Create(name, value);
        }

        return new Secrets(values);
    }

    private static void CheckKeys(JsonElement value, string label, string[] allowed, string[] unsupported, Dictionary<string, string>? moved)
    {
        foreach (var property in value.EnumerateObject())
        {
            var key = property.Name;
            if (allowed.Contains(key, StringComparer.Ordinal))
            {
                continue;
            }

            var where = label == "config" ? "config key" : label + " key";
            if (unsupported.Contains(key, StringComparer.Ordinal))
            {
                throw Invalid(where + " \"" + key + "\" is not supported by the .NET port");
            }

            string hint;
            if (moved is not null && moved.TryGetValue(key, out var movedHint))
            {
                hint = "; " + movedHint;
            }
            else
            {
                var suggestion = DidYouMean(key, allowed);
                hint = suggestion is null ? "" : "; did you mean \"" + suggestion + "\"?";
            }

            throw Invalid("unknown " + where + " \"" + key + "\"" + hint);
        }
    }

    private static string? DidYouMean(string key, string[] candidates)
    {
        string? best = null;
        var bestDistance = int.MaxValue;
        foreach (var candidate in candidates)
        {
            var distance = Distance(key.ToLowerInvariant(), candidate.ToLowerInvariant());
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return bestDistance <= Math.Max(2, key.Length / 3) && bestDistance < key.Length ? best : null;
    }

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + cost);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }

    private static JsonElement? Property(JsonElement value, string name)
    {
        return value.TryGetProperty(name, out var property) ? property : null;
    }

    private static string? OptionalString(JsonElement value, string name, string label)
    {
        if (Property(value, name) is not { } property)
        {
            return null;
        }

        var text = property.ValueKind == JsonValueKind.String ? property.GetString() : null;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw Invalid(label + " must be a non-empty string");
        }

        return text;
    }

    // A string that may be empty, as upstream system and context.
    private static string? AnyString(JsonElement value, string name, string label)
    {
        if (Property(value, name) is not { } property)
        {
            return null;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            throw Invalid(label + " must be a string");
        }

        return property.GetString();
    }

    private static TimeSpan Milliseconds(JsonElement root, string name, TimeSpan fallback)
    {
        return Property(root, name) is { } raw ? TimeSpan.FromMilliseconds(Integer(raw, name, 1, int.MaxValue)) : fallback;
    }

    private static int Integer(JsonElement value, string label, int min, int max)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number) || number < min || number > max)
        {
            throw Invalid(max == int.MaxValue
                ? label + " must be a positive integer"
                : label + " must be an integer from " + min.ToString(CultureInfo.InvariantCulture) + " to " + max.ToString(CultureInfo.InvariantCulture));
        }

        return number;
    }

    private static bool ValidName(string name)
    {
        return name.Length > 0
            && name.Any(ch => ch != '.')
            && name.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '_' or '-' or '.');
    }

    private static ConfigurationException Invalid(string message) => new("INVALID_CONFIG", message);
}

/// <summary>One entry of <c>targets</c>. The port drives the <c>web</c> platform.</summary>
public sealed class TargetConfig
{
    public required string Name { get; init; }

    public required string Platform { get; init; }

    public required AppConfig App { get; init; }
}

public sealed class AppConfig
{
    /// <summary>Base URL for <c>App.OpenAsync</c>. Relative URLs resolve against it.</summary>
    public string? Url { get; init; }
}

/// <summary>
/// One entry of <c>agents</c>. Upstream <c>model</c> and <c>judge</c> are AI SDK instances; here they are model ids
/// for <see cref="Provider"/>, which share <see cref="BaseUrl"/> and <see cref="ApiKeyEnv"/>.
/// Each entry starts from the defaults; none inherits another's values.
/// </summary>
public sealed class AgentConfig
{
    public const string DefaultProvider = "openai";

    /// <summary>
    /// The <c>provider</c> values: API keys (<c>openai</c>, <c>openai-responses</c>, <c>azure</c>, <c>anthropic</c>,
    /// <c>google</c>, <c>bedrock</c>, <c>xai</c>, <c>openrouter</c>, <c>gateway</c>, <c>openai-compatible</c>) and
    /// subscription logins (<c>chatgpt</c>, <c>copilot</c>, <c>grok</c>, <c>opencode-console</c>).
    /// </summary>
    public static IReadOnlyList<string> Providers { get; } =
    [
        "openai", "openai-responses", "azure", "anthropic", "google", "bedrock", "xai", "openrouter", "gateway", "openai-compatible",
        "chatgpt", "copilot", "grok", "opencode-console",
    ];

    public string? Model { get; init; }

    /// <summary>The model id that judges <c>assert</c>, <c>waitFor</c>, and <c>extract</c>. Unset, the judge is <see cref="Model"/>.</summary>
    public string? Judge { get; init; }

    /// <summary>Who serves <see cref="Model"/> and <see cref="Judge"/>. One of <see cref="Providers"/>; <c>openai</c> by default.</summary>
    public string Provider { get; init; } = DefaultProvider;

    /// <summary>Text appended to the act rules. Judges never see it.</summary>
    public string? System { get; init; }

    /// <summary>Project context told to every model call, at most 16384 UTF-8 bytes.</summary>
    public string? Context { get; init; }

    /// <summary>The API root. Unset, the provider's own; for <c>azure</c> it is built from <c>AZURE_RESOURCE_NAME</c>.</summary>
    public string? BaseUrl { get; init; }

    /// <summary>The environment variable holding the key. Unset, the one the provider reads, such as <c>ANTHROPIC_API_KEY</c>.</summary>
    public string? ApiKeyEnv { get; init; }

    /// <summary>1 through 100.</summary>
    public int MaxSteps { get; init; } = E2EDefaults.MaxSteps;

    /// <summary>1 through 100.</summary>
    public int MaxModelCalls { get; init; } = E2EDefaults.MaxModelCalls;

    public TimeSpan JudgmentTimeout { get; init; } = E2EDefaults.JudgmentTimeout;

    /// <summary>Provider options by provider. Each client adds the entry under its own key to its request body.</summary>
    public IReadOnlyDictionary<string, JsonElement>? ProviderOptions { get; init; }

    /// <summary>The model for <see cref="Model"/>, or null when no model is set.</summary>
    public IAgentModel? CreateModel() => Create(Model);

    /// <summary>The model for <see cref="Judge"/>, or null when no judge is set.</summary>
    public IAgentModel? CreateJudge() => Create(Judge);

    /// <summary>The <see cref="AgentOptions"/> this entry describes, with its models created.</summary>
    public AgentOptions CreateOptions()
    {
        return new AgentOptions
        {
            Model = CreateModel(),
            Judge = CreateJudge(),
            System = System,
            Context = Context,
            MaxSteps = MaxSteps,
            MaxModelCalls = MaxModelCalls,
            JudgmentTimeout = JudgmentTimeout,
            ProviderOptions = ProviderOptions,
        };
    }

    internal static bool IsSubscription(string provider) => provider is "chatgpt" or "copilot" or "grok" or "opencode-console";

    private IAgentModel? Create(string? model)
    {
        if (model is null)
        {
            return null;
        }

        return Provider switch
        {
            "openai-responses" => new OpenAiResponsesModel(new OpenAiResponsesModelOptions
            {
                Model = model,
                BaseUrl = BaseUrl ?? "https://api.openai.com/v1",
                ApiKeyEnv = ApiKeyEnv ?? "OPENAI_API_KEY",
            }),
            "azure" => new OpenAiResponsesModel(new OpenAiResponsesModelOptions
            {
                Model = model,
                BaseUrl = BaseUrl ?? AzureBaseUrl(),
                Provider = "azure",
                ApiKeyEnv = ApiKeyEnv ?? "AZURE_API_KEY",
                ApiKeyHeader = "api-key",
                QueryParameters = new Dictionary<string, string>(StringComparer.Ordinal) { ["api-version"] = "v1" },
            }),
            "anthropic" => new AnthropicModel(new AnthropicModelOptions
            {
                Model = model,
                BaseUrl = BaseUrl ?? "https://api.anthropic.com/v1",
                ApiKeyEnv = ApiKeyEnv ?? "ANTHROPIC_API_KEY",
            }),
            "google" => new GoogleModel(new GoogleModelOptions
            {
                Model = model,
                BaseUrl = BaseUrl ?? "https://generativelanguage.googleapis.com/v1beta",
                ApiKeyEnv = ApiKeyEnv ?? "GOOGLE_GENERATIVE_AI_API_KEY",
            }),
            "bedrock" => new BedrockModel(new BedrockModelOptions
            {
                Model = model,
                BaseUrl = BaseUrl,
                ApiKeyEnv = ApiKeyEnv ?? "AWS_BEARER_TOKEN_BEDROCK",
            }),
            "xai" => Compatible(model, "https://api.x.ai/v1", "xai", "XAI_API_KEY", null, null),
            "openrouter" => Compatible(model, "https://openrouter.ai/api/v1", "openrouter", "OPENROUTER_API_KEY", null, ModelProviders.AttributionHeaders),
            "gateway" => Compatible(model, "https://ai-gateway.vercel.sh/v1", "gateway", "AI_GATEWAY_API_KEY", "VERCEL_OIDC_TOKEN", ModelProviders.AttributionHeaders),
            "openai-compatible" => Compatible(model, "http://127.0.0.1:11434/v1", "openai-compatible", "LLM_API_KEY", null, null),
            "chatgpt" => OAuth.Subscriptions.ChatGpt(model),
            "copilot" => OAuth.Subscriptions.Copilot(model),
            "grok" => OAuth.Subscriptions.Grok(model),
            "opencode-console" => OAuth.Subscriptions.OpenCodeConsole(model),
            _ => Compatible(model, "https://api.openai.com/v1", "openai", "OPENAI_API_KEY", null, null),
        };
    }

    private OpenAiCompatibleModel Compatible(string model, string baseUrl, string provider, string apiKeyEnv, string? fallbackEnv, IReadOnlyDictionary<string, string>? headers)
    {
        return new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions
        {
            Model = model,
            BaseUrl = BaseUrl ?? baseUrl,
            Provider = provider,
            ApiKeyEnv = ApiKeyEnv ?? apiKeyEnv,
            ApiKeyFallbackEnv = ApiKeyEnv is null ? fallbackEnv : null,
            Headers = headers,
        });
    }

    private static string AzureBaseUrl()
    {
        var resource = Environment.GetEnvironmentVariable("AZURE_RESOURCE_NAME");
        return string.IsNullOrWhiteSpace(resource)
            ? throw new ConfigurationException("INVALID_CONFIG", "provider \"azure\" needs baseUrl or AZURE_RESOURCE_NAME")
            : "https://" + resource.Trim() + ".openai.azure.com/openai/v1";
    }
}

public sealed class CacheConfig
{
    /// <summary>Unset, it is <see cref="CacheMode.ReadWrite"/>, or <see cref="CacheMode.ReadOnly"/> in CI.</summary>
    public CacheMode Mode { get; init; } = CacheMode.ReadWrite;

    /// <summary>Absolute cache directory. <c>cache.dir</c> resolves against the config directory; the default is <c>.e2e/cache</c>.</summary>
    public string Directory { get; init; } = Path.Combine(".e2e", "cache");

    /// <summary>When true, a recording that no longer matches fails with <c>REPLAY_STALE</c> instead of running live.</summary>
    public bool Strict { get; init; }
}

/// <summary>How the replay cache is used.</summary>
public enum CacheMode
{
    /// <summary>Never read or write recordings.</summary>
    Off,

    /// <summary>Replay recordings, never write or delete them.</summary>
    ReadOnly,

    /// <summary>Replay recordings, record verified acts, and delete unverified ones.</summary>
    ReadWrite,
}
