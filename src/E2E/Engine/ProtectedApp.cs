// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace E2E.Engine;

/// <summary>What an app that sits behind headers, basic auth, or a user agent may be sent.</summary>
internal static partial class ProtectedApp
{
    // An HTTP header field name: one or more token characters (RFC 9110).
    [GeneratedRegex(@"^[!#$%&'*+.^_`|~0-9A-Za-z-]+\z")]
    internal static partial Regex HeaderNamePattern();

    // A control character no HTTP field value may carry; a horizontal tab is the one the grammar allows.
    [GeneratedRegex(@"[\u0000-\u0008\u000A-\u001F\u007F]")]
    internal static partial Regex FieldValueControlPattern();
}
