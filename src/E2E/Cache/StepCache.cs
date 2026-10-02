// Copyright 2026 TesterArmy.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using E2E.Internal;

namespace E2E;

/// <summary>
/// A recording of one verified <c>act</c>. The key is the test, the instruction,
/// the params, and the engine's major.minor version. The model id is not part of the key.
/// </summary>
public sealed class CacheEntry
{
    public int Schema { get; set; } = 1;

    public string? Test { get; set; }

    public string? Instruction { get; set; }

    public string? Route { get; set; }

    public string? EndRoute { get; set; }

    public List<RecordedAction> Actions { get; set; } = [];

    public List<RecordedTarget> Appeared { get; set; } = [];
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
}

public sealed class RecordedTarget
{
    public string? Role { get; set; }

    public string? Name { get; set; }

    public string? TestId { get; set; }
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
    public static string Create(
        string engine,
        string version,
        string test,
        string instruction,
        IReadOnlyDictionary<string, object?>? parameters)
    {
        var builder = new StringBuilder();
        builder.Append(engine).Append('\n').Append(MajorMinor(version)).Append('\n');
        builder.Append(test).Append('\n').Append(instruction.Trim()).Append('\n');
        if (parameters is not null)
        {
            foreach (var key in parameters.Keys.OrderBy(item => item, StringComparer.Ordinal))
            {
                builder.Append(key).Append('=').Append(Canonical(parameters[key])).Append('\n');
            }
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Canonical(object? value)
    {
        return value switch
        {
            null => "",
            UniqueValue => "<unique>",
            Secret secret => "<secret:" + secret.Name + ">",
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? "",
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
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? "",
            _ => value.ToString() ?? "",
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

    private static string MajorMinor(string version)
    {
        var parts = version.Split('.');
        if (parts.Length == 0 || parts[0].Length == 0)
        {
            return version;
        }

        return parts.Length == 1 ? parts[0] : parts[0] + "." + parts[1];
    }
}
