// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Collections;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace E2E.Internal;

/// <summary>
/// Checks <c>agent.act</c> params as upstream does: JSON-safe, at most 32 levels deep, no cycle,
/// and at most 64 KiB once projected to JSON. A <see cref="Secret"/> projects to its name and
/// purpose, and a <see cref="UniqueValue"/> to its value.
/// </summary>
internal static class ActParams
{
    public const int MaxBytes = 65_536;

    public const int MaxDepth = 32;

    private const string Label = "agent.act params";

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static void Validate(IReadOnlyDictionary<string, object?>? parameters)
    {
        if (parameters is null)
        {
            return;
        }

        var projected = Project(parameters, 0, new HashSet<object>(ReferenceEqualityComparer.Instance));
        var bytes = Encoding.UTF8.GetByteCount(projected?.ToJsonString(Json) ?? "null");
        if (bytes > MaxBytes)
        {
            throw Invalid(Label + " are " + bytes.ToString(CultureInfo.InvariantCulture) + " canonical bytes; the maximum is " + MaxBytes.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static JsonNode? Project(object? value, int depth, HashSet<object> ancestors)
    {
        if (depth > MaxDepth)
        {
            throw Invalid(Label + " exceeds " + MaxDepth.ToString(CultureInfo.InvariantCulture) + " levels of nesting");
        }

        switch (value)
        {
            case null:
                return null;
            case Secret secret:
                var projected = new JsonObject { ["kind"] = "secret", ["name"] = secret.Name };
                if (secret.Purpose is not null)
                {
                    projected["purpose"] = secret.Purpose;
                }

                return projected;
            case UniqueValue unique:
                return JsonValue.Create(unique.Value);
            case string text:
                return JsonValue.Create(text);
            case double number when !double.IsFinite(number):
            case float single when !float.IsFinite(single):
                throw Invalid(Label + " contains a non-finite number");
            case JsonElement element:
                return Element(element, depth);
            case JsonNode node:
                return Element(JsonSerializer.SerializeToElement(node, Json), depth);
        }

        var info = Json.GetTypeInfo(value.GetType());
        if (info.Kind == JsonTypeInfoKind.None)
        {
            try
            {
                return JsonSerializer.SerializeToNode(value, value.GetType(), Json);
            }
            catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
            {
                throw Invalid(Label + " must be JSON-safe: " + ex.Message);
            }
        }

        if (!value.GetType().IsValueType && !ancestors.Add(value))
        {
            throw Invalid(Label + " contains a cycle");
        }

        try
        {
            switch (info.Kind)
            {
                case JsonTypeInfoKind.Dictionary:
                    var map = new JsonObject();
                    foreach (var (key, entry) in Entries((IEnumerable)value))
                    {
                        map[key] = Project(entry, depth + 1, ancestors);
                    }

                    return map;
                case JsonTypeInfoKind.Enumerable:
                    var list = new JsonArray();
                    foreach (var entry in (IEnumerable)value)
                    {
                        list.Add(Project(entry, depth + 1, ancestors));
                    }

                    return list;
                default:
                    var shape = new JsonObject();
                    foreach (var property in info.Properties)
                    {
                        if (property.Get is { } get)
                        {
                            shape[property.Name] = Project(get(value), depth + 1, ancestors);
                        }
                    }

                    return shape;
            }
        }
        finally
        {
            ancestors.Remove(value);
        }
    }

    private static JsonNode? Element(JsonElement element, int depth)
    {
        if (depth > MaxDepth)
        {
            throw Invalid(Label + " exceeds " + MaxDepth.ToString(CultureInfo.InvariantCulture) + " levels of nesting");
        }

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var map = new JsonObject();
                foreach (var property in element.EnumerateObject())
                {
                    map[property.Name] = Element(property.Value, depth + 1);
                }

                return map;
            case JsonValueKind.Array:
                var list = new JsonArray();
                foreach (var item in element.EnumerateArray())
                {
                    list.Add(Element(item, depth + 1));
                }

                return list;
            case JsonValueKind.Undefined or JsonValueKind.Null:
                return null;
            default:
                return JsonValue.Create(element);
        }
    }

    private static IEnumerable<(string Key, object? Value)> Entries(IEnumerable dictionary)
    {
        if (dictionary is IDictionary plain)
        {
            foreach (DictionaryEntry entry in plain)
            {
                yield return (Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? "", entry.Value);
            }

            yield break;
        }

        foreach (var item in dictionary)
        {
            var type = item!.GetType();
            var key = type.GetProperty("Key")?.GetValue(item);
            yield return (Convert.ToString(key, CultureInfo.InvariantCulture) ?? "", type.GetProperty("Value")?.GetValue(item));
        }
    }

    private static TestException Invalid(string message) => new("INVALID_ARGUMENT", message);
}
