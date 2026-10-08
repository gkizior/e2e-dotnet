// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>
/// Anchors: the step's recorded delta. The nodes a step made appear (<see cref="CacheEntry.Appeared"/>)
/// and the nodes it made vanish (<see cref="CacheEntry.Gone"/>), each with the value and states a step
/// sets on a control it leaves on screen. A replay passes on its own only when the delta happened again:
/// everything that appeared is there, everything that vanished is gone, at least one of those changes
/// happened during the replay (<see cref="Evidenced"/>), and no alert shows that the recording never saw
/// (<see cref="Holds"/>). Presence is enough; uniqueness is not asked.
/// </summary>
internal static class Anchors
{
    /// <summary>The most anchors one side of the delta records.</summary>
    public const int Max = 8;

    /// <summary>The states a step sets on a control. Focus is where the pointer went, not what the step did.</summary>
    private static readonly string[] States = ["checked", "expanded", "pressed", "selected"];

    /// <summary>
    /// Roles an app announces an outcome in: the live regions a screen reader reads out on change, and
    /// the alert dialog. They are recorded before any other anchor, so the cap never crowds them out.
    /// </summary>
    private static readonly HashSet<string> AnnouncementRoles = new(["alert", "alertdialog", "status"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The announcements that interrupt. A replay that raises one the recording never saw has another
    /// outcome than the recording, so each one is checked both ways.
    /// </summary>
    private static readonly HashSet<string> AlertRoles = new(["alert", "alertdialog"], StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Text that cannot read the same on the next run: a minted id, a countdown or age, a date, a clock
    /// time. An anchor made of it hands every replay off, so it is skipped while some stable anchor exists;
    /// with nothing else, the volatile ones stay, because a replay that always hands off is still safer than
    /// one that passes on mechanics alone. An alert keeps it, and is compared with these parts read as one
    /// placeholder each (<see cref="AlertShape"/>).
    /// </summary>
    private static readonly Regex[] VolatileSpans =
    [
        new(@"\b(?=[A-Za-z0-9_-]*\d)(?=[A-Za-z0-9_-]*[A-Za-z])[A-Za-z0-9_-]{12,}\b", RegexOptions.CultureInvariant),
        new(@"\b\d+\s*(?:ms|s|secs?|seconds?|m|mins?|minutes?|h|hrs?|hours?|d|days?|weeks?|months?|years?)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        new(@"\b(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*\.?\s+\d{1,2}(?:,?\s+\d{4})?\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        new(@"\b\d{1,2}\s+(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*\.?(?:\s+\d{4})?\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        new(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant),
        new(@"\b\d{1,2}:\d{2}(?::\d{2})?\b", RegexOptions.CultureInvariant),
    ];

    /// <summary>
    /// A count that names what it counts: a pagination range, <c>12 items</c>, <c>3 records imported</c>.
    /// It is the step's own result when the step made it appear on the screen it acted on. It is the data
    /// instead, which the next run reads differently, when the other side of the delta already showed the
    /// same text with other numbers, or when the step moved to another route.
    /// </summary>
    private static readonly Regex[] CountText =
    [
        new(@"\b\d+\s*(?:to|-|\u2013)\s*\d+\s+of\s+\d+\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
        new(@"\b\d+\s+(?:results?|items?|rows?|entries|records?|matches|total)\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase),
    ];

    private static readonly Regex BareNumber = new(@"^\d+$", RegexOptions.CultureInvariant);

    private static readonly Regex Digits = new(@"\d+", RegexOptions.CultureInvariant);

    /// <summary>
    /// Every node of an observation that can serve as an anchor, in document order: a visible node with a
    /// name, a text, a test id, or a placeholder. The text is kept only when it differs from the name: for an
    /// anchor the text is the effect, a status reading "saved" rather than "empty". <paramref name="skip"/>
    /// drops a node whose name or text the next run reads differently, and <paramref name="record"/> is the
    /// form a name, text, or value is stored in, such as with secrets redacted, and <paramref name="redact"/> the
    /// form a test id or placeholder is. A secure field's value is never read.
    /// </summary>
    public static List<AnchorNode> Project(Observation observation, Func<string, bool> skip, Func<string, string> record, Func<string, string> redact)
    {
        static string? Field(string? value) => value is null || TextRules.Normalize(value) is not { Length: > 0 } text ? null : text;
        var anchors = new List<AnchorNode>();
        foreach (var node in LocatorResolver.Walk(observation.Roots))
        {
            var name = Field(node.Name);
            var text = Field(node.Text) is { } read && read != name ? read : null;
            var testId = Field(node.TestId);
            var placeholder = Field(node.Placeholder);
            if (node.States.Hidden || (name is null && text is null && testId is null && placeholder is null)
                || (name is not null && skip(name)) || (text is not null && skip(text)))
            {
                continue;
            }

            var value = node.Value is null || node.States.Secure ? null : Field(node.Value);
            var states = States.Where(state => StateOf(node.States, state)).ToList();
            var target = new RecordedTarget
            {
                Role = Field(node.Role),
                Name = name is null ? null : record(name),
                Text = text is null ? null : record(text),
                TestId = testId is null ? null : redact(testId),
                Placeholder = placeholder is null ? null : redact(placeholder),
                InputPurpose = node.InputPurpose is null or "none" ? null : node.InputPurpose,
                Value = value is null ? null : record(value),
                States = states.Count == 0 ? null : states,
            };
            anchors.Add(new AnchorNode(target, KeyOf(target), node.Children.Count == 0));
        }

        return anchors;
    }

    /// <summary>
    /// One step's delta between its start and end screens, each side ordered announcements first, then
    /// leaves, then containers, each in document order, deduplicated and capped. A container's name is
    /// usually its children's names joined, so it repeats what the leaves already say, and on a list-heavy
    /// screen those repeats would crowd the one status line that names the effect out of the cap. A node counts as appeared only when no start
    /// node matches its anchor, so the anchor cannot hold before the step ran. A node counts as gone only when no end node
    /// matches its anchor the way <see cref="Holds"/> checks one: a radio "Express" the pick replaced
    /// with a status reading "Express" vanished, but an anchor of that name would still match the status,
    /// and the recording would fail its own check on every replay. <paramref name="routeMoved"/> says the step
    /// ended on another route than it began on, whose counts are that screen's data rather than the step's result.
    /// </summary>
    public static (List<RecordedTarget> Appeared, List<RecordedTarget> Gone) Describe(IReadOnlyList<AnchorNode> start, IReadOnlyList<AnchorNode> end, bool routeMoved)
    {
        var startKeys = start.Select(anchor => anchor.Key).ToHashSet(StringComparer.Ordinal);
        var endKeys = end.Select(anchor => anchor.Key).ToHashSet(StringComparer.Ordinal);
        return (
            Side(end, start, anchor => startKeys.Contains(anchor.Key) || In(anchor.Target, start), routeMoved),
            Side(start, end, anchor => endKeys.Contains(anchor.Key) || In(anchor.Target, end), routeMoved));
    }

    /// <summary>
    /// Whether the recorded end state holds on <paramref name="live"/>: every appeared anchor present,
    /// no gone anchor present, and no alert on screen that was not on <paramref name="before"/> and is
    /// not one the recording saw appear.
    /// </summary>
    public static bool Holds(CacheEntry entry, IReadOnlyList<AnchorNode> live, IReadOnlyList<AnchorNode> before)
    {
        if (!entry.Appeared.All(anchor => In(anchor, live)) || entry.Gone.Any(anchor => In(anchor, live)))
        {
            return false;
        }

        var earlier = before.Where(anchor => IsAlert(anchor.Target)).ToList();
        return live.All(anchor =>
            !IsAlert(anchor.Target)
            || In(anchor.Target, earlier)
            || entry.Appeared.Any(recorded => Matches(recorded, anchor.Target)));
    }

    /// <summary>
    /// Whether the replay itself produced some of the recorded delta, measured from
    /// <paramref name="baseline"/>, the first screen the replay saw on the route it ended on: an anchor
    /// that appeared was absent there, or a gone one was there. A recording with no delta is never
    /// evidence. A null baseline means the replay saw no screen on that route before its last action, so
    /// that action moved the route, which is the evidence.
    /// <para>
    /// A control one of the recorded actions targeted (<paramref name="inputTargets"/>) that shows on both
    /// sides of the delta, a field typed into or a switch flipped, echoes the input. Such echoes are
    /// evidence only when the delta holds nothing else.
    /// </para>
    /// </summary>
    public static bool Evidenced(CacheEntry entry, IReadOnlyList<AnchorNode>? baseline, IEnumerable<RecordedTarget> inputTargets)
    {
        if (baseline is null)
        {
            return true;
        }

        var targets = inputTargets.Select(IdentityKey).ToHashSet(StringComparer.Ordinal);
        var goneIdentities = entry.Gone.Select(IdentityKey).ToHashSet(StringComparer.Ordinal);
        var changed = entry.Appeared.Select(IdentityKey).Where(goneIdentities.Contains).ToHashSet(StringComparer.Ordinal);
        bool Echoes(RecordedTarget anchor) => changed.Contains(IdentityKey(anchor)) && targets.Contains(IdentityKey(anchor));
        var outcome = entry.Appeared.Concat(entry.Gone).Any(anchor => !Echoes(anchor));
        bool Counted(RecordedTarget anchor) => !outcome || !Echoes(anchor);
        return entry.Appeared.Any(anchor => Counted(anchor) && !In(anchor, baseline))
            || entry.Gone.Any(anchor => Counted(anchor) && In(anchor, baseline));
    }

    private static List<RecordedTarget> Side(IReadOnlyList<AnchorNode> nodes, IReadOnlyList<AnchorNode> other, Func<AnchorNode, bool> inOther, bool routeMoved)
    {
        var otherShapes = other.Select(anchor => CountShape(anchor.Target)).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var announcements = new List<RecordedTarget>();
        var leaves = new List<RecordedTarget>();
        var containers = new List<RecordedTarget>();
        foreach (var anchor in nodes)
        {
            if (!seen.Add(anchor.Key) || inOther(anchor))
            {
                continue;
            }

            (anchor.Target.Role is { } role && AnnouncementRoles.Contains(role) ? announcements : anchor.Leaf ? leaves : containers).Add(anchor.Target);
        }

        var all = announcements.Concat(leaves).Concat(containers).ToList();
        var stable = all.Where(anchor => IsAlert(anchor) || !IsVolatile(anchor, count => routeMoved || otherShapes.Contains(CountShape(count)))).ToList();
        return (stable.Count > 0 ? stable : all).Take(Max).ToList();
    }

    /// <summary>
    /// Whether an anchor's text cannot read the same on the next run; <paramref name="countIsData"/> decides an
    /// anchor holding a count. A bare number is a badge or a tally, <c>11</c> unread, and is volatile when it is
    /// all the anchor has to say; the same digit inside a named control, a status labelled "Counter" that reads
    /// <c>1</c>, is the step's effect and stays.
    /// </summary>
    private static bool IsVolatile(RecordedTarget anchor, Func<RecordedTarget, bool> countIsData)
    {
        var texts = new[] { anchor.Text, anchor.Name, anchor.Value }.OfType<string>().ToList();
        var label = anchor.Name ?? anchor.Text;
        var bare = label is not null && BareNumber.IsMatch(label) && anchor.TestId is null
            && (anchor.Name is null || anchor.Text is null || anchor.Name == anchor.Text);
        if (bare || texts.Any(text => VolatileSpans.Any(pattern => pattern.IsMatch(text))))
        {
            return true;
        }

        return texts.Any(text => CountText.Any(pattern => pattern.IsMatch(text))) && countIsData(anchor);
    }

    /// <summary>An anchor's key with every digit run read as one placeholder: the text a running count keeps while its numbers move.</summary>
    private static string CountShape(RecordedTarget anchor)
    {
        return KeyOf(new RecordedTarget
        {
            Role = anchor.Role,
            Name = anchor.Name is null ? null : Digits.Replace(anchor.Name, "#"),
            Text = anchor.Text is null ? null : Digits.Replace(anchor.Text, "#"),
            TestId = anchor.TestId,
            Placeholder = anchor.Placeholder,
            InputPurpose = anchor.InputPurpose,
            Value = anchor.Value is null ? null : Digits.Replace(anchor.Value, "#"),
            States = anchor.States,
        });
    }

    /// <summary>
    /// Whether some node equals a recorded anchor on every field the anchor recorded (role, name, text, test
    /// id, placeholder, input purpose), and on its value and states exactly: an empty field is not the field
    /// filled in, nor an unchecked switch the switch turned on. A test id is forgiven when the other fields
    /// still identify the node, since some apps mint them per render. An alert is compared by its shape.
    /// </summary>
    private static bool In(RecordedTarget anchor, IReadOnlyList<AnchorNode> nodes)
    {
        return nodes.Any(node => Matches(anchor, node.Target));
    }

    private static bool Matches(RecordedTarget anchor, RecordedTarget node)
    {
        if (!string.Equals(anchor.Value, node.Value, StringComparison.Ordinal) || StatesKey(anchor) != StatesKey(node))
        {
            return false;
        }

        var recorded = Shape(anchor);
        var candidate = Shape(node);
        bool Same(string? field, string? other) => field is null || string.Equals(field, other, StringComparison.Ordinal);
        var semantic = Same(recorded.Role, candidate.Role) && Same(recorded.Name, candidate.Name) && Same(recorded.Text, candidate.Text)
            && Same(recorded.Placeholder, candidate.Placeholder) && Same(recorded.InputPurpose, candidate.InputPurpose);
        return semantic && (Same(recorded.TestId, candidate.TestId) || Identifies(Semantic(anchor)));
    }

    // Whether the fields other than the test id still tell the node apart.
    private static bool Identifies(RecordedTarget anchor) => anchor.Name is not null || anchor.Text is not null || anchor.Placeholder is not null;

    private static RecordedTarget Semantic(RecordedTarget anchor) => new()
    {
        Role = anchor.Role,
        Name = anchor.Name,
        Text = anchor.Text,
        Placeholder = anchor.Placeholder,
        InputPurpose = anchor.InputPurpose,
        Value = anchor.Value,
        States = anchor.States,
    };

    // An alert with its volatile parts read as placeholders; any other anchor as it is.
    private static RecordedTarget Shape(RecordedTarget anchor)
    {
        if (!IsAlert(anchor))
        {
            return anchor;
        }

        return new RecordedTarget
        {
            Role = anchor.Role,
            Name = anchor.Name is null ? null : AlertShape(anchor.Name),
            Text = anchor.Text is null ? null : AlertShape(anchor.Text),
            TestId = anchor.TestId,
            Placeholder = anchor.Placeholder,
            InputPurpose = anchor.InputPurpose,
            Value = anchor.Value,
            States = anchor.States,
        };
    }

    private static bool IsAlert(RecordedTarget anchor)
    {
        return anchor.Role is { } role && AlertRoles.Contains(role);
    }

    /// <summary>
    /// An alert text with its volatile parts read as one placeholder each: <c>Session expires at 17:42</c>
    /// and <c>Session expires at 17:45</c> are the same alert.
    /// </summary>
    private static string AlertShape(string text)
    {
        return VolatileSpans.Aggregate(text, (masked, span) => span.Replace(masked, "#"));
    }

    /// <summary>
    /// Set key for one anchor. An anchor the other fields identify is keyed without its test id, so an app that mints test ids
    /// per render cannot make every unchanged control look new; a node only a test id identifies keeps it.
    /// The states and the value make a control that changed a node of the delta on both sides: the switch
    /// as it was, gone, and as it is, appeared. An alert is keyed by its shape, so one whose countdown
    /// ticked while the step ran stayed on screen.
    /// </summary>
    private static string KeyOf(RecordedTarget anchor)
    {
        return IdentityKey(anchor) + "\n" + anchor.Value + "\n" + StatesKey(anchor);
    }

    /// <summary>
    /// A control's key without what a step sets on it, its value and states: the same control before and after.
    /// It is the anchor's loosest identifying form, without the test id whenever the other fields identify
    /// the node.
    /// </summary>
    private static string IdentityKey(RecordedTarget anchor)
    {
        var shape = Shape(anchor);
        var testId = Identifies(shape) ? null : shape.TestId;
        return string.Join('\n', shape.Role, shape.Name, shape.Text, testId, shape.Placeholder, shape.InputPurpose);
    }

    private static string StatesKey(RecordedTarget anchor)
    {
        return anchor.States is null ? "" : string.Join(',', anchor.States);
    }

    private static bool StateOf(NodeStates states, string state)
    {
        return state switch
        {
            "checked" => states.Checked,
            "expanded" => states.Expanded,
            "pressed" => states.Pressed,
            _ => states.Selected,
        };
    }
}

/// <summary>One node projected as an anchor, with its set key and whether it has no children.</summary>
internal sealed record AnchorNode(RecordedTarget Target, string Key, bool Leaf);
