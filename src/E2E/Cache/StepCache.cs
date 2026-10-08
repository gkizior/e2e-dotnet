// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using E2E.Internal;

namespace E2E;

/// <summary>
/// A recording of one verified <c>act</c>. The key is the cache schema, the replay
/// policy version, the engine, the test, the instruction, the params, the agent's name,
/// a digest of the agent's redacted context, and which repeat of that same call in the
/// attempt it is. The model id is not part of the key.
/// </summary>
public sealed class CacheEntry
{
    public int Schema { get; set; } = 1;

    public string? Test { get; set; }

    public string? Instruction { get; set; }

    public string? Route { get; set; }

    public string? EndRoute { get; set; }

    public List<RecordedAction> Actions { get; set; } = [];

    /// <summary>Nodes on screen when the step passed and not when it began (<see cref="Anchors"/>).</summary>
    public List<RecordedTarget> Appeared { get; set; } = [];

    /// <summary>Nodes on screen when the step began and gone when it passed.</summary>
    public List<RecordedTarget> Gone { get; set; } = [];
}

public sealed class RecordedAction
{
    public string Kind { get; set; } = "";

    public string? Role { get; set; }

    public string? Name { get; set; }

    public string? TestId { get; set; }

    public string? Value { get; set; }

    public string? Key { get; set; }

    public string? Url { get; set; }

    /// <summary><c>up</c>, <c>down</c>, <c>left</c>, or <c>right</c> for <c>scroll</c> and <c>scrollUntil</c>.</summary>
    public string? Direction { get; set; }

    /// <summary>Consecutive identical scrolls folded into one action. Null means one.</summary>
    public int? Times { get; set; }

    /// <summary>The text a <c>scrollUntil</c> paged toward.</summary>
    public string? Text { get; set; }
}

public sealed class RecordedTarget
{
    public string? Role { get; set; }

    public string? Name { get; set; }

    public string? TestId { get; set; }

    /// <summary>An anchor's text when it differs from its name, with secrets redacted.</summary>
    public string? Text { get; set; }

    /// <summary>An anchor's placeholder.</summary>
    public string? Placeholder { get; set; }

    /// <summary>An anchor's input purpose, such as <c>password</c>. Null for <c>none</c>.</summary>
    public string? InputPurpose { get; set; }

    /// <summary>An anchor's value, with secrets redacted. Null for an empty or secure field.</summary>
    public string? Value { get; set; }

    /// <summary>An anchor's <c>checked</c>, <c>expanded</c>, <c>pressed</c>, and <c>selected</c> states that are on. Null for none.</summary>
    public List<string>? States { get; set; }
}

public sealed class CacheLookup
{
    public CacheEntry? Entry { get; init; }

    /// <summary><c>no-entry</c> or <c>invalid-entry</c> when <see cref="Entry"/> is null.</summary>
    public string? Reason { get; init; }
}

public interface IStepCache
{
    CacheLookup Read(string key);

    void Write(string key, CacheEntry entry);

    void Delete(string key);
}

/// <summary>JSON files in a directory, one per cache key. Entries over 1 MiB are ignored.</summary>
public sealed class FileStepCache : IStepCache
{
    public const int SchemaVersion = 1;

    private readonly string _directory;

    public FileStepCache(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    public CacheLookup Read(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
        {
            return new CacheLookup { Reason = "no-entry" };
        }

        var info = new FileInfo(path);
        if (info.Length > 1024 * 1024)
        {
            return new CacheLookup { Reason = "invalid-entry" };
        }

        try
        {
            var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(path), JsonDefaults.Options);
            if (entry is null || entry.Schema != SchemaVersion)
            {
                return new CacheLookup { Reason = "invalid-entry" };
            }

            return new CacheLookup { Entry = entry };
        }
        catch (JsonException)
        {
            return new CacheLookup { Reason = "invalid-entry" };
        }
        catch (IOException)
        {
            return new CacheLookup { Reason = "invalid-entry" };
        }
    }

    public void Write(string key, CacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Directory.CreateDirectory(_directory);
        var path = PathFor(key);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(entry, JsonDefaults.Options));
        File.Move(temp, path, overwrite: true);
    }

    public void Delete(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string key)
    {
        if (key.Length == 0 || key.Any(ch => !char.IsAsciiLetterOrDigit(ch)))
        {
            throw new TestException("INVALID_ARGUMENT", "Cache key must be ASCII letters and digits.");
        }

        return Path.Combine(_directory, key + ".json");
    }
}

internal static class CacheKeys
{
    /// <summary>
    /// Version of the rules that decide whether a recording replays. Bump it when those
    /// rules change, so old entries become misses instead of wrong replays.
    /// </summary>
    public const string ReplayPolicyVersion = "2";

    private static readonly JsonSerializerOptions KeyJson = Json(new LeafConverter<Secret>(Canonical), new LeafConverter<UniqueValue>(Canonical));

    private static readonly JsonSerializerOptions DisplayJson = Json(new LeafConverter<Secret>(Display), new LeafConverter<UniqueValue>(Display));

    public static string Create(
        string engine,
        string test,
        string instruction,
        IReadOnlyDictionary<string, object?>? parameters,
        string agent,
        string? agentContext)
    {
        var builder = new StringBuilder();
        builder.Append("schema=").Append(FileStepCache.SchemaVersion.ToString(CultureInfo.InvariantCulture)).Append('\n');
        builder.Append("policy=").Append(ReplayPolicyVersion).Append('\n');
        builder.Append(engine).Append('\n');
        builder.Append(test).Append('\n').Append(instruction.Trim()).Append('\n');
        if (parameters is not null)
        {
            foreach (var key in parameters.Keys.OrderBy(item => item, StringComparer.Ordinal))
            {
                builder.Append(key).Append('=').Append(Canonical(parameters[key])).Append('\n');
            }
        }

        // The agent acts as one person with one context, so another agent, or a changed context, records again.
        // The context enters as a digest of its redacted text, so a secret in it contributes only its name.
        builder.Append("agent=").Append(agent).Append('\n');
        builder.Append("context=").Append(Hash(agentContext ?? "")).Append('\n');
        return Hash(builder.ToString());
    }

    /// <summary>
    /// The key of one repeat of a signature from <see cref="Create"/>. The first call
    /// uses the signature itself, and each later identical call gets its own key.
    /// </summary>
    public static string ForCall(string signature, int callIndex)
    {
        return callIndex == 0 ? signature : Hash(signature + "\n" + callIndex.ToString(CultureInfo.InvariantCulture));
    }

    public static string Canonical(object? value)
    {
        return value switch
        {
            null => "",
            UniqueValue => "<unique>",
            Secret secret => "<secret:" + secret.Name + ">",
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => Json(value),
        };
    }

    public static string Display(object? value)
    {
        return value switch
        {
            null => "",
            Secret secret when secret.Purpose is null => secret.ToString(),
            Secret secret => secret + " (" + secret.Purpose + ")",
            UniqueValue unique => unique.Value,
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => JsonSerializer.Serialize(value, DisplayJson),
        };
    }

    public static string Template(string value, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null)
        {
            return value;
        }

        foreach (var pair in parameters)
        {
            if (pair.Value is UniqueValue unique && unique.Value.Length > 0 && value.Contains(unique.Value, StringComparison.Ordinal))
            {
                value = value.Replace(unique.Value, "\u0001" + pair.Key + "\u0001", StringComparison.Ordinal);
            }
        }

        return value;
    }

    public static string Detemplate(string value, IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null)
        {
            return value;
        }

        foreach (var pair in parameters)
        {
            if (pair.Value is UniqueValue unique)
            {
                value = value.Replace("\u0001" + pair.Key + "\u0001", unique.Value, StringComparison.Ordinal);
            }
        }

        return value;
    }

    public static bool Collides(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null)
        {
            return false;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var value in parameters.Values)
        {
            var text = value switch
            {
                UniqueValue unique => unique.Value,
                string textValue => textValue,
                _ => null,
            };
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (!seen.Add(text))
            {
                return true;
            }
        }

        return false;
    }

    private static string Hash(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string Json(object value)
    {
        try
        {
            return JsonSerializer.Serialize(value, KeyJson);
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            throw new TestException("INVALID_ARGUMENT", "Act params must serialize as JSON: " + ex.Message, ex);
        }
    }

    private static JsonSerializerOptions Json(params JsonConverter[] converters)
    {
        var options = new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        foreach (var converter in converters)
        {
            options.Converters.Add(converter);
        }

        return options;
    }
}

/// <summary>Writes a nested <see cref="Secret"/> or <see cref="UniqueValue"/> param as one string.</summary>
internal sealed class LeafConverter<T>(Func<T, string> write) : JsonConverter<T>
{
    public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        throw new NotSupportedException();
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(write(value));
    }
}
