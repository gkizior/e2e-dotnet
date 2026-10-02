// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>
/// Semantic queries against the current screen. An action or a single-node
/// assertion fails when the query matches more than one node. Narrow it with
/// <see cref="Locator.Filter"/>, <see cref="Locator.First"/>, or <see cref="Locator.Nth"/>.
/// </summary>
public sealed class Screen
{
    private readonly Func<CancellationToken, Task<Observation>> _observe;
    private readonly Func<SemanticNode, LocatorAction, CancellationToken, Task> _perform;
    private readonly Func<CancellationToken> _cancellation;
    private readonly Action _verified;

    internal Screen(
        Func<CancellationToken, Task<Observation>> observe,
        Func<SemanticNode, LocatorAction, CancellationToken, Task> perform,
        Func<CancellationToken> cancellation,
        Action verified,
        TimeSpan assertionTimeout)
    {
        _observe = observe;
        _perform = perform;
        _cancellation = cancellation;
        _verified = verified;
        AssertionTimeout = assertionTimeout;
    }

    internal TimeSpan AssertionTimeout { get; }

    internal TimeSpan PollInterval { get; } = TimeSpan.FromMilliseconds(50);

    public Locator GetByRole(string role, string? name = null, bool exact = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return new Locator(this, new LocatorQuery
        {
            Kind = "role",
            Role = role,
            Name = name,
            Exact = exact,
        });
    }

    public Locator GetByText(string text, bool exact = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        return new Locator(this, new LocatorQuery { Kind = "text", Text = text, Exact = exact });
    }

    public Locator GetByLabel(string label, bool exact = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return new Locator(this, new LocatorQuery { Kind = "label", Name = label, Exact = exact });
    }

    public Locator GetByTestId(string testId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testId);
        return new Locator(this, new LocatorQuery { Kind = "testid", TestId = testId });
    }

    public Locator GetByPlaceholder(string placeholder, bool exact = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placeholder);
        return new Locator(this, new LocatorQuery { Kind = "placeholder", Name = placeholder, Exact = exact });
    }

    internal Task<Observation> ObserveAsync(CancellationToken cancellationToken)
    {
        return _observe(cancellationToken == default ? _cancellation() : cancellationToken);
    }

    internal Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken)
    {
        return _perform(node, action, cancellationToken == default ? _cancellation() : cancellationToken);
    }

    internal CancellationToken Token(CancellationToken cancellationToken)
    {
        return cancellationToken == default ? _cancellation() : cancellationToken;
    }

    internal void NotifyVerified() => _verified();
}

/// <summary>A query that resolves against a fresh observation every time it is used.</summary>
public sealed class Locator
{
    private readonly Screen _screen;

    internal Locator(Screen screen, LocatorQuery query)
    {
        _screen = screen;
        Query = query;
    }

    internal LocatorQuery Query { get; }

    public Locator Filter(string hasText)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hasText);
        return new Locator(_screen, Query.With(hasText: hasText));
    }

    public Locator First() => Nth(0);

    public Locator Nth(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new Locator(_screen, Query.With(index: index));
    }

    public Locator GetByRole(string role, string? name = null, bool exact = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        return new Locator(_screen, new LocatorQuery
        {
            Parent = Query,
            Kind = "role",
            Role = role,
            Name = name,
            Exact = exact,
        });
    }

    public Task TapAsync(CancellationToken cancellationToken = default)
    {
        return ActAsync(new LocatorAction.Tap(), cancellationToken);
    }

    public Task FillAsync(string value, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(value);
        return ActAsync(new LocatorAction.Fill(value), cancellationToken);
    }

    public Task PressAsync(string key, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return ActAsync(new LocatorAction.Press(key), cancellationToken);
    }

    public Task CheckAsync(CancellationToken cancellationToken = default)
    {
        return ActAsync(new LocatorAction.Check(), cancellationToken);
    }

    public Task UncheckAsync(CancellationToken cancellationToken = default)
    {
        return ActAsync(new LocatorAction.Uncheck(), cancellationToken);
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return ActAsync(new LocatorAction.Clear(), cancellationToken);
    }

    /// <summary>Reads text once. Does not retry and does not verify an earlier <c>act</c>.</summary>
    public async Task<string?> TextContentAsync(CancellationToken cancellationToken = default)
    {
        var node = await ResolveStrictAsync(cancellationToken).ConfigureAwait(false);
        return node.Text ?? node.Name;
    }

    /// <summary>Reads an input value once. Does not retry and does not verify an earlier <c>act</c>.</summary>
    public async Task<string?> InputValueAsync(CancellationToken cancellationToken = default)
    {
        var node = await ResolveStrictAsync(cancellationToken).ConfigureAwait(false);
        return node.Value;
    }

    internal async Task<IReadOnlyList<SemanticNode>> ResolveAsync(CancellationToken cancellationToken)
    {
        var observation = await _screen.ObserveAsync(cancellationToken).ConfigureAwait(false);
        return LocatorResolver.Resolve(observation, Query);
    }

    internal Screen Screen => _screen;

    private async Task ActAsync(LocatorAction action, CancellationToken cancellationToken)
    {
        var node = await ResolveStrictAsync(cancellationToken).ConfigureAwait(false);
        if (node.States.Disabled)
        {
            throw new TestException("NOT_ACTIONABLE", Query.Describe() + " is disabled.");
        }

        await _screen.PerformAsync(node, action, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SemanticNode> ResolveStrictAsync(CancellationToken cancellationToken)
    {
        var token = _screen.Token(cancellationToken);
        var matches = await ResolveAsync(token).ConfigureAwait(false);
        if (matches.Count == 0)
        {
            throw new TestException("NOT_FOUND", Query.Describe() + " matched 0 nodes.");
        }

        if (matches.Count > 1)
        {
            throw new TestException("STRICT_MODE", Query.Describe() + " matched " + matches.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " nodes.");
        }

        return matches[0];
    }
}

internal sealed class LocatorQuery
{
    public LocatorQuery? Parent { get; init; }

    public string Kind { get; init; } = "role";

    public string? Role { get; init; }

    public string? Name { get; init; }

    public bool Exact { get; init; } = true;

    public string? TestId { get; init; }

    public string? Text { get; init; }

    public string? HasText { get; init; }

    public int? Index { get; init; }

    public LocatorQuery With(string? hasText = null, int? index = null)
    {
        return new LocatorQuery
        {
            Parent = Parent,
            Kind = Kind,
            Role = Role,
            Name = Name,
            Exact = Exact,
            TestId = TestId,
            Text = Text,
            HasText = hasText ?? HasText,
            Index = index ?? Index,
        };
    }

    public string Describe()
    {
        var core = Kind switch
        {
            "role" => Name is null ? "getByRole(\"" + Role + "\")" : "getByRole(\"" + Role + "\", \"" + Name + "\")",
            "text" => "getByText(\"" + Text + "\")",
            "label" => "getByLabel(\"" + Name + "\")",
            "testid" => "getByTestId(\"" + TestId + "\")",
            "placeholder" => "getByPlaceholder(\"" + Name + "\")",
            _ => Kind,
        };
        if (HasText is not null)
        {
            core += ".filter(hasText: \"" + HasText + "\")";
        }

        if (Index is int nth)
        {
            core += ".nth(" + nth.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")";
        }

        return core;
    }
}

internal static class LocatorResolver
{
    private static readonly HashSet<string> LabelRoles = new(StringComparer.OrdinalIgnoreCase)
    {
        "textbox",
        "checkbox",
        "radio",
        "combobox",
        "searchbox",
    };

    public static IReadOnlyList<SemanticNode> Resolve(Observation observation, LocatorQuery query)
    {
        var matches = new List<SemanticNode>();
        if (query.Parent is null)
        {
            foreach (var root in observation.Roots)
            {
                Collect(root, query, matches);
            }
        }
        else
        {
            foreach (var parent in Resolve(observation, query.Parent))
            {
                foreach (var child in parent.Children)
                {
                    Collect(child, query, matches);
                }
            }
        }

        if (query.HasText is not null)
        {
            matches = matches.Where(node => ContainsText(node, query.HasText)).ToList();
        }

        if (query.Index is int index)
        {
            if (index < 0 || index >= matches.Count)
            {
                return [];
            }

            return [matches[index]];
        }

        return matches;
    }

    public static IEnumerable<SemanticNode> Walk(IEnumerable<SemanticNode> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in Walk(root.Children))
            {
                yield return child;
            }
        }
    }

    private static void Collect(SemanticNode node, LocatorQuery query, List<SemanticNode> matches)
    {
        if (!node.States.Hidden && Matches(node, query))
        {
            matches.Add(node);
        }

        foreach (var child in node.Children)
        {
            Collect(child, query, matches);
        }
    }

    private static bool Matches(SemanticNode node, LocatorQuery query)
    {
        return query.Kind switch
        {
            "role" => RoleIs(node, query.Role) && (query.Name is null || TextRules.Matches(node.Name, query.Name, query.Exact)),
            "text" => TextRules.Matches(node.Text, query.Text ?? "", query.Exact) || TextRules.Matches(node.Name, query.Text ?? "", query.Exact),
            "label" => node.Role is not null && LabelRoles.Contains(node.Role) && TextRules.Matches(node.Name, query.Name ?? "", query.Exact),
            "testid" => string.Equals(node.TestId, query.TestId, StringComparison.Ordinal),
            "placeholder" => TextRules.Matches(node.Placeholder, query.Name ?? "", query.Exact),
            _ => false,
        };
    }

    private static bool RoleIs(SemanticNode node, string? role)
    {
        return node.Role is not null && role is not null && string.Equals(node.Role, role, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsText(SemanticNode node, string expected)
    {
        if (TextRules.Contains(node.Name, expected) || TextRules.Contains(node.Text, expected) || TextRules.Contains(node.Value, expected))
        {
            return true;
        }

        foreach (var child in node.Children)
        {
            if (ContainsText(child, expected))
            {
                return true;
            }
        }

        return false;
    }
}
