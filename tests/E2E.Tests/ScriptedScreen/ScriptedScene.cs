// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ScriptedScreen;

/// <summary>
/// The scripted screen behind upstream's scripted engine: a mutable semantic tree whose nodes react to actions
/// with hooks and whose script schedules mutations (<see cref="After"/>) so polling has something to wait for.
/// It plays the engine under a <see cref="E2E.Screen"/>: it answers observations from the live tree, applies
/// plausible in-memory semantics to each action, and records every action it performed.
/// The app is not open until <see cref="Open"/>, so an observation before that is <c>INVALID_STATE</c>.
/// </summary>
internal sealed class ScriptedScene : IDisposable
{
    private readonly List<ScriptedNode> _nodes;
    private readonly List<Timer> _timers = [];
    private readonly object _gate = new();
    private bool _opened;

    public ScriptedScene(Func<ScriptedScene, IEnumerable<ScriptedNode>> build)
    {
        _nodes = [.. build(this)];
        Screen = new Screen(
            _ => Task.FromResult(Observe()),
            (node, action, _) =>
            {
                Perform(node.Ref, action);
                return Task.CompletedTask;
            },
            () => CancellationToken.None,
            () => { },
            TimeSpan.FromMilliseconds(500),
            TimeSpan.FromMilliseconds(500));
    }

    public Screen Screen { get; }

    /// <summary>Every action the scene performed, as the node id and the action.</summary>
    public List<(string Id, LocatorAction Action)> Performs { get; } = [];

    /// <summary>Runs <paramref name="mutate"/> once, <paramref name="milliseconds"/> from now; disposing the scene cancels it.</summary>
    public void After(int milliseconds, Action mutate) =>
        _timers.Add(new Timer(_ => { lock (_gate) { mutate(); } }, null, milliseconds, Timeout.Infinite));

    public void Open() => _opened = true;

    public void Dispose()
    {
        foreach (var timer in _timers)
        {
            timer.Dispose();
        }
    }

    private static SemanticNode ToSemantic(ScriptedNode node) => new()
    {
        Ref = node.Id,
        Role = node.Role,
        Name = node.Name,
        Text = node.Text,
        Value = node.Secure ? null : node.Value,
        TestId = node.TestId,
        InputPurpose = node.InputPurpose,
        Level = node.Level,
        States = new NodeStates
        {
            Checked = node.Checked,
            Disabled = node.Disabled,
            Expanded = node.Expanded,
            Focused = node.Focused,
            Hidden = node.Hidden,
            Secure = node.Secure,
            Selected = node.Selected,
            Pressed = node.Pressed,
        },
        Attributes = node.Attributes,
        Rect = node.Rect,
        Children = node.Children.Select(ToSemantic).ToList(),
    };

    private static IEnumerable<ScriptedNode> All(IEnumerable<ScriptedNode> level)
    {
        foreach (var node in level)
        {
            yield return node;
            foreach (var child in All(node.Children))
            {
                yield return child;
            }
        }
    }

    public Observation Observe()
    {
        lock (_gate)
        {
            return ObserveLocked();
        }
    }

    private Observation ObserveLocked()
    {
        if (!_opened)
        {
            throw new EngineException(EngineErrorCodes.InvalidState, "no app is open; call app.open() first");
        }

        return new Observation { Route = "/", Roots = _nodes.Select(ToSemantic).ToList() };
    }

    public void Perform(string id, LocatorAction action)
    {
        lock (_gate)
        {
            PerformLocked(id, action);
        }
    }

    private void PerformLocked(string id, LocatorAction action)
    {
        var node = All(_nodes).FirstOrDefault(entry => entry.Id == id)
            ?? throw new EngineException(EngineErrorCodes.NodeStale, "node " + id + " is no longer in the tree");
        Performs.Add((id, action));
        Apply(node, action);
        node.On?.Invoke(action, node);
    }

    private void Apply(ScriptedNode node, LocatorAction action)
    {
        switch (action)
        {
            case LocatorAction.Fill fill:
                node.Value = fill.Value;
                break;
            case LocatorAction.Clear:
                node.Value = "";
                break;
            case LocatorAction.Check:
                node.Checked = true;
                break;
            case LocatorAction.Uncheck:
                node.Checked = false;
                break;
            case LocatorAction.Focus:
                Focus(node);
                break;
            case LocatorAction.PressSequentially typed:
                Focus(node);
                node.Value = (node.Value ?? "") + typed.Text;
                break;
            case LocatorAction.Press { Key: "Backspace" }:
                node.Value = (node.Value ?? "")[..Math.Max(0, (node.Value ?? "").Length - 1)];
                break;
            case LocatorAction.Press { Key.Length: 1 } press:
                node.Value = (node.Value ?? "") + press.Key;
                break;
            case LocatorAction.Select select:
                var picked = node.Options.FirstOrDefault(option => option.Label == select.Value)
                    ?? node.Options.FirstOrDefault(option => option.Value == select.Value)
                    ?? throw new EngineException(EngineErrorCodes.NotActionable, "no option matches " + select.Value + " on " + node.Id);
                node.Value = picked.Value;
                break;
        }
    }

    private void Focus(ScriptedNode target)
    {
        foreach (var node in All(_nodes))
        {
            node.Focused = false;
        }

        target.Focused = true;
    }
}
