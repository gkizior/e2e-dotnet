// Copyright 2026 TesterArmy.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;

namespace E2E.Internal;

internal static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
