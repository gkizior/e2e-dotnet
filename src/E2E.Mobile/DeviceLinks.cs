// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;

namespace E2E.Mobile;

/// <summary>
/// The links <c>device.OpenLinkAsync</c> takes: the scheme rule the runner applies to every navigation,
/// the label a link enters the report under, and the guard that keeps a link out of
/// <c>device.OpenAppAsync</c>.
/// <para>
/// The runner's own rule first demands an app URL, which a device engine never declares, and admits
/// http(s) alone, where a device link is any app's custom scheme. So the check here is a list of refused
/// schemes: the ones that read local content or inline a document, and the browser wrappers
/// (<c>view-source:file:...</c>, <c>blob:</c>, <c>filesystem:</c>) that carry one inside.
/// </para>
/// </summary>
public static partial class DeviceLinks
{
    /// <summary>The C0 controls and the space, which a browser trims off both ends of a URL.</summary>
    private static readonly char[] EdgeCharacters = [.. Enumerable.Range(0, 0x21).Select(code => (char)code)];

    private static readonly HashSet<string> ForbiddenProtocols = ["file:", "data:", "javascript:", "view-source:", "blob:", "filesystem:"];

    /// <summary>
    /// What agent-device opens as a URL instead of resolving as an app: a <c>scheme:rest</c> string with
    /// no whitespace. Every string the device would route as a link matches; a display name with a space
    /// (<c>Notes: Pro</c>) does not.
    /// </summary>
    [GeneratedRegex(@"^([A-Za-z][A-Za-z0-9+.-]*):\S+\z")]
    private static partial Regex LinkShape();

    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9+.-]*:")]
    private static partial Regex SchemePrefix();

    /// <summary>Whether agent-device would open this string as a URL rather than resolve it as an app.</summary>
    public static bool IsLink(string value) => LinkShape().IsMatch(value.Trim());

    /// <summary>
    /// Parses a link a test asked to open. A string that is not an absolute URL is
    /// <c>INVALID_ARGUMENT</c>; a <c>file:</c>, <c>data:</c>, <c>javascript:</c>, <c>view-source:</c>,
    /// <c>blob:</c>, or <c>filesystem:</c> link is <c>POLICY_DENIED</c>, as it is for <c>app.open</c> on the
    /// web. The message never echoes the input: a malformed link can carry a magic-link token in its query,
    /// path, or userinfo, and nothing about a string that failed to parse says which part is safe to repeat.
    /// </summary>
    public static Uri LinkTarget(string input)
    {
        // As a browser reads a URL: edge whitespace and control characters go, and so do tabs and line breaks anywhere.
        var text = new string(input.Where(character => character is not ('\t' or '\n' or '\r')).ToArray()).Trim(EdgeCharacters);
        if (!SchemePrefix().IsMatch(text) || !Uri.TryCreate(text, UriKind.Absolute, out var url))
        {
            throw new TestException(
                "INVALID_ARGUMENT",
                "openLink needs an absolute URL such as myapp://orders/42 or https://example.com/verify");
        }

        DenyForbiddenScheme(url.Scheme + ":");
        return url;
    }

    /// <summary>
    /// Refuses a link handed to <c>openApp</c>, which opens an app by bundle id, package, or display name.
    /// agent-device opens any <c>scheme:rest</c> string as a URL, so the check runs before the device sees
    /// it: a link with a forbidden scheme is <c>POLICY_DENIED</c> as it is for <c>openLink</c>, any other
    /// link is <c>INVALID_ARGUMENT</c> pointing at <c>openLink</c>. The message never echoes the link,
    /// whose query may carry a magic-link token.
    /// </summary>
    public static void AssertAppId(string app)
    {
        var link = LinkShape().Match(app.Trim());
        if (!link.Success)
        {
            return;
        }

        DenyForbiddenScheme(link.Groups[1].Value.ToLowerInvariant() + ":");
        throw new TestException(
            "INVALID_ARGUMENT",
            "openApp opens an app by bundle id, package, or display name; open a deep link or web link with device.openLink");
    }

    /// <summary>
    /// The link as the report shows it, cut before its query and fragment. A magic link carries its
    /// one-time token there, and a step label is kept for as long as the report is.
    /// </summary>
    public static string LinkLabel(string input)
    {
        var cut = input.AsSpan().IndexOfAny('?', '#');
        return cut < 0 ? input : input[..cut];
    }

    private static void DenyForbiddenScheme(string protocol)
    {
        if (ForbiddenProtocols.Contains(protocol))
        {
            throw new ConfigurationException("POLICY_DENIED", $"forbidden URL scheme: {protocol}");
        }
    }
}
