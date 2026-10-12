// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using System.Text.Json.Nodes;
using E2E.Engine;

namespace E2E.Mobile;

/// <summary>Who acts through a <see cref="DeviceSession"/>: it decides whether an action settles, and whether it waits out a transition.</summary>
public enum ActionOrigin
{
    /// <summary>A test's own step. It verifies its outcome with <c>expect</c>, so it never waits for the screen to settle afterwards. It only lets a control that came with the last action finish arriving.</summary>
    Test,

    /// <summary>The agent. It reads the screen right after acting, so its actions settle first.</summary>
    Agent,
}

/// <summary>Action data only: a bound node never retains a projected snapshot through parent links.</summary>
internal sealed record NodeBinding(string Id, string Ref, SemanticNode Node, string ControlRef, IReadOnlyDictionary<string, bool> States);

public sealed partial class DeviceSession
{
    /// <summary>
    /// How far the dismissing drag travels, as a share of the viewport: short enough that a list under the finger
    /// barely moves, long enough for a scroll view to read it as a drag and let go of the keyboard. The same ratio
    /// Maestro's <c>hideKeyboard</c> uses.
    /// </summary>
    private const double KeyboardDragRatio = 0.03;

    /// <summary>Platform element types that are the soft keyboard or one of its keys, as agent-device names them.</summary>
    private static readonly HashSet<string> KeyboardTypes = ["keyboard", "key"];

    private Dictionary<string, NodeBinding> _generation = [];
    private IReadOnlyList<ProjectedNode>? _latestIndex;

    /// <summary>The screen as last projected before the last action, to tell controls that were already there from ones that came with it.</summary>
    private IReadOnlyList<ProjectedNode>? _indexBeforeAction;

    /// <summary>When this session last acted on the device (an input or an app launch). The screen may be in transition for a while after.</summary>
    private DateTimeOffset _lastActionAt = DateTimeOffset.MinValue;

    /// <summary>The screen as the last look projected it. An action records it as the screen before the action.</summary>
    internal IReadOnlyList<ProjectedNode>? LatestIndex => _latestIndex;

    /// <summary>What agent-device is told about settling after an action: the quiet window, or nothing when <c>Settle</c> is off.</summary>
    internal JsonObject SettleArgs() =>
        Options.Settle
            ? DeviceArgs.Of(("settle", JsonValue.Create(true)), ("settleQuietMs", JsonValue.Create(Options.SettleMilliseconds)))
            : new JsonObject();

    /// <summary>The settle wait one action of this session carries: a test's step carries none.</summary>
    internal JsonObject ActionSettle() => Origin == ActionOrigin.Test ? new JsonObject() : SettleArgs();

    /// <summary>
    /// Runs a fixture command that changes the screen (a back, the home screen, a dismissed alert or keyboard, a
    /// rotation) and records it as an action, so a control that arrives with the change waits out the transition
    /// budget before a test acts on it, as it does after a tap. Without this a <c>device.back()</c> followed by a tap
    /// lands where the returning screen has not yet arrived.
    /// </summary>
    internal async Task<T> ScreenCommandAsync<T>(string label, Func<IDeviceClient, CancellationToken, Task<T>> run, CancellationToken cancellationToken)
    {
        var before = _latestIndex;
        var result = await CommandAsync(label, run, cancellationToken).ConfigureAwait(false);
        MarkAction(before);
        return result;
    }

    /// <summary>
    /// Records an action the device received: the screen as it was projected before it, and when it returned. The
    /// transition budget counts from the return, since the input lands late in the command's own round trip.
    /// </summary>
    internal void MarkAction(IReadOnlyList<ProjectedNode>? before)
    {
        _indexBeforeAction = before;
        _lastActionAt = _engine.Clock.GetUtcNow();
    }

    /// <summary>
    /// Forgets the screen after a launch or a close. There is no screen before it worth matching against: every
    /// control of whatever shows next is arriving, whatever the previous screen looked like, and none of its
    /// bindings names a node that still exists.
    /// </summary>
    private void ScreenReplaced()
    {
        MarkAction(null);
        _generation = [];
    }

    /// <summary>The binding of a node of the newest observation, or <c>NODE_STALE</c>.</summary>
    internal NodeBinding Resolve(SemanticNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return _generation.TryGetValue(node.Ref, out var entry)
            ? entry
            : throw new EngineException(EngineErrorCodes.NodeStale, $"node {node.Ref} is not part of the newest observation", retryable: true);
    }

    /// <summary>The target of a command: the node's agent-device ref, or its toggle's inner control when <paramref name="control"/> is set.</summary>
    internal static (string Key, JsonNode? Value) ActionTarget(NodeBinding entry, bool control = false)
    {
        var reference = control ? entry.ControlRef : entry.Ref;
        return reference.Length == 0
            ? throw DeviceSupport.NotActionable($"node {entry.Id} has no agent-device ref to act on")
            : ("ref", JsonValue.Create("@" + reference));
    }

    /// <summary>
    /// The node a toggle press must land on. UIKit reports a settings row as a labelled <c>Switch</c> spanning the
    /// whole row with the real control as an unlabelled <c>Switch</c> child at its trailing edge. A press at the
    /// row's centre hits the label and changes nothing. The innermost same-role descendant with a ref is the control.
    /// A node without one is its own.
    /// </summary>
    private static ProjectedNode ControlOf(ProjectedNode entry, IReadOnlyList<ProjectedNode> snapshot)
    {
        var role = entry.Node.Role;
        if (role is not ("switch" or "checkbox"))
        {
            return entry;
        }

        var control = entry;
        var depth = 0;
        foreach (var candidate in snapshot)
        {
            if (candidate.Ref.Length == 0 || candidate.Node.Role != role || !SnapshotProjection.IsWithin(candidate, entry))
            {
                continue;
            }

            var candidateDepth = DepthBelow(candidate, entry);
            if (candidateDepth > depth)
            {
                control = candidate;
                depth = candidateDepth;
            }
        }

        return control;
    }

    /// <summary>Number of parent hops from <paramref name="entry"/> up to <paramref name="ancestor"/>.</summary>
    private static int DepthBelow(ProjectedNode entry, ProjectedNode ancestor)
    {
        var hops = 0;
        for (var current = entry.Parent; current is not null; current = current.Parent)
        {
            hops++;
            if (ReferenceEquals(current, ancestor))
            {
                return hops;
            }
        }

        return hops;
    }

    /// <summary>Copies the fields actions use and pre-resolves a toggle's inner control.</summary>
    private static NodeBinding Bind(ProjectedNode entry, IReadOnlyList<ProjectedNode> snapshot) =>
        new(entry.Id, entry.Ref, entry.Node, ControlOf(entry, snapshot).Ref, entry.States);

    /// <summary>The node a binding stands for, in a fresh snapshot: same role, test id, name, and text, and of those the one closest to where it was.</summary>
    private static ProjectedNode? Refind(NodeBinding entry, IReadOnlyList<ProjectedNode> index)
    {
        var candidates = index
            .Where(candidate =>
                candidate.Node.Role == entry.Node.Role
                && candidate.Node.TestId == entry.Node.TestId
                && candidate.Node.Name == entry.Node.Name
                && candidate.Node.Text == entry.Node.Text)
            .ToList();
        if (candidates.Count <= 1 || entry.Node.Rect is not { } rect)
        {
            return candidates.Count == 0 ? null : candidates[0];
        }

        var was = CentreOf(rect);
        var best = candidates[0];
        var bestDistance = double.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            if (candidate.Node.Rect is not { } box)
            {
                continue;
            }

            var at = CentreOf(box);
            var distance = Math.Sqrt(Math.Pow(at.X - was.X, 2) + Math.Pow(at.Y - was.Y, 2));
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private static Point CentreOf(BoundingBox box) => new(box.X + (box.Width / 2), box.Y + (box.Height / 2));

    /// <summary>Whether two rects are the same to the pixel. A missing rect never matches, so a target must be measurable to be stable.</summary>
    private static bool SameRect(BoundingBox? a, BoundingBox? b) =>
        a is not null && b is not null
        && Math.Abs(a.X - b.X) < 1 && Math.Abs(a.Y - b.Y) < 1 && Math.Abs(a.Width - b.Width) < 1 && Math.Abs(a.Height - b.Height) < 1;

    /// <summary>
    /// Lets a control that came with the last action finish arriving before a test acts on it, and answers the
    /// binding to act on. A control already present at the same place in the snapshot the last action was resolved
    /// from is not in transition and is acted on at once. Anything else waits out the remainder of the transition
    /// budget since that action and is then found again in a fresh snapshot. The budget is the only signal that the
    /// transition is over, since the tree cannot tell a sliding control from a landed one. The fresh snapshot is where
    /// the control landed: Android reports a window's frames while it slides in, so a ref resolved mid-flight would
    /// send the action where the control was.
    /// </summary>
    internal async Task<NodeBinding> SettledAsync(NodeBinding entry, CancellationToken cancellationToken)
    {
        var since = _engine.Clock.GetUtcNow() - _lastActionAt;
        var remaining = TimeSpan.FromMilliseconds(Options.TransitionMilliseconds) - since;
        if (remaining <= TimeSpan.Zero)
        {
            return entry;
        }

        if (_indexBeforeAction is { } before && Refind(entry, before) is { } prior && SameRect(prior.Node.Rect, entry.Node.Rect))
        {
            return entry;
        }

        try
        {
            await _engine.Delay(remaining, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw DeviceSupport.Cancelled("transition wait cancelled");
        }

        // The wait resolves on cancellation. The action behind it must not go out once the caller has already been told
        // the operation was cancelled.
        if (cancellationToken.IsCancellationRequested)
        {
            throw DeviceSupport.Cancelled("transition wait cancelled");
        }

        return await RelocatedAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The same control in a fresh snapshot. The binding as it was when the snapshot no longer lists it.</summary>
    private async Task<NodeBinding> RelocatedAsync(NodeBinding entry, CancellationToken cancellationToken)
    {
        var projected = Project(await SnapshotOrEmptyAsync(false, cancellationToken).ConfigureAwait(false));
        return Refind(entry, projected.Index) is { } found ? Bind(found, projected.Index) : entry;
    }

    /// <summary>
    /// Runs one action's device command, abandoned when the caller's token fires. A ref agent-device refuses as stale
    /// becomes a retryable <c>NODE_STALE</c>.
    /// </summary>
    internal async Task ActAsync(string label, Func<IDeviceClient, CancellationToken, Task> run, CancellationToken cancellationToken)
    {
        var client = Client;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await run(client, cancellationToken).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception cause)
        {
            throw DeviceErrors.StaleOr(cause, label, _where);
        }
    }

    // --- the keyboard ---

    /// <summary>
    /// Whether a capture shows the soft keyboard: the band its producer measured when it measured one, else a keyboard
    /// or key element in the tree, the probe Maestro's <c>hideKeyboard</c> uses. A capture that says nothing either way
    /// counts as no keyboard: a drag on a guess costs more than a keyboard a later tap refuses against by name.
    /// </summary>
    private static bool KeyboardShowing(RawSnapshot raw) =>
        raw.KeyboardKind switch
        {
            "visible" => true,
            "absent" => false,
            _ => raw.Nodes.Any(node => KeyboardTypes.Contains((node.Type ?? "").ToLowerInvariant())),
        };

    /// <summary>
    /// Hides the soft keyboard, so a control it covered can be reached. agent-device presses the keyboard's own
    /// dismiss key and refuses when the keyboard has none (an iPhone), because a touch outside the keyboard is a guess
    /// about the screen it never makes on a caller's behalf. The engine makes that guess, the way a user does and
    /// Maestro's <c>hideKeyboard</c> does, in <see cref="DismissKeyboardByGestureAsync"/>.
    /// </summary>
    public async Task DismissKeyboardAsync(CancellationToken cancellationToken)
    {
        var before = _latestIndex;
        try
        {
            await CommandAsync("keyboard.dismiss", DeviceMethods.CommandKeyboard, DeviceArgs.Of(("action", JsonValue.Create("dismiss"))), cancellationToken).ConfigureAwait(false);
        }
        catch (EngineException cause) when (cause.Code == EngineErrorCodes.UnsupportedCapability)
        {
            await DismissKeyboardByGestureAsync(cancellationToken).ConfigureAwait(false);
        }

        MarkAction(before);
    }

    /// <summary>
    /// A keyboard with no dismiss key goes the way a user sends it away: a short drag at the centre of the screen,
    /// which a scroll view turns into a dismissal, each followed by a fresh look at whether the keyboard is still there.
    /// Horizontal first: on a vertical scroll view it is no scroll, so React Native's scroll view takes it as the touch
    /// outside the keyboard that dismisses by default, where a vertical drag scrolls the content and dismisses only
    /// under <c>keyboardDismissMode="on-drag"</c>. Vertical second, for the scroll views that dismiss on a drag alone.
    /// The simulator's "Speed up your typing" tip, which sits over a keyboard the first time it appears, goes through
    /// its Continue button first. A drag is a guess about what sits at the centre, so the sequence stops the moment
    /// the keyboard is gone, and a keyboard that outlives both drags fails naming what does work. Each look is the
    /// observation's own capture, sparse-tree retries included: a one-node tree has no keys and would pass for a
    /// dismissed keyboard.
    /// </summary>
    private async Task DismissKeyboardByGestureAsync(CancellationToken cancellationToken)
    {
        var raw = await DismissKeyboardTipAsync(await SnapshotAsync(false, cancellationToken).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);
        if (!KeyboardShowing(raw))
        {
            return;
        }

        var projected = SnapshotProjection.ProjectSnapshot(raw.Nodes, () => "keyboard", raw.Viewport);
        var viewport = await ViewportForAsync(projected, cancellationToken).ConfigureAwait(false);
        var centre = new Point(viewport.Width / 2, viewport.Height / 2);
        Point[] drags =
        [
            new(centre.X - (viewport.Width * KeyboardDragRatio), centre.Y),
            new(centre.X, centre.Y - (viewport.Height * KeyboardDragRatio)),
        ];
        foreach (var to in drags)
        {
            await CommandAsync(
                "keyboard.dismiss",
                DeviceMethods.InteractionsSwipe,
                DeviceArgs.Of(("from", PointNode(centre)), ("to", PointNode(to))),
                cancellationToken).ConfigureAwait(false);
            raw = await SnapshotAsync(false, cancellationToken).ConfigureAwait(false);
            if (!KeyboardShowing(raw))
            {
                return;
            }
        }

        throw new EngineException(
            EngineErrorCodes.UnsupportedCapability,
            "keyboard dismissal failed: the keyboard has no dismiss key and stayed up through a horizontal and a vertical drag at the centre of the screen. Tap the app's own Done or close control, or press Enter on the field when submitting is what you want.",
            retryable: false);
    }

    /// <summary>
    /// The simulator shows a "Speed up your typing" tip over the keyboard the first time it appears. Its Continue
    /// button is the only way past it, and Maestro's <c>hideKeyboard</c> presses it too. Answers the capture after the
    /// press, or the one given when no tip is showing.
    /// </summary>
    private async Task<RawSnapshot> DismissKeyboardTipAsync(RawSnapshot raw, CancellationToken cancellationToken)
    {
        var tip = raw.Nodes.FirstOrDefault(node => (node.Label ?? node.Value ?? "").StartsWith("Speed up your typing", StringComparison.Ordinal));
        if (tip?.Rect is not { } tipRect)
        {
            return raw;
        }

        var button = raw.Nodes.FirstOrDefault(node => node.Label == "Continue" && node.Rect is { } rect && rect.Y > tipRect.Y);
        if (button?.Rect is not { } buttonRect)
        {
            return raw;
        }

        var centre = new Point(buttonRect.X + (buttonRect.Width / 2), buttonRect.Y + (buttonRect.Height / 2));
        await CommandAsync(
            "keyboard.dismiss",
            DeviceMethods.InteractionsPress,
            DeviceArgs.Of(("x", JsonValue.Create(centre.X)), ("y", JsonValue.Create(centre.Y))),
            cancellationToken).ConfigureAwait(false);
        return await SnapshotAsync(false, cancellationToken).ConfigureAwait(false);
    }

    internal static JsonObject PointNode(Point point) => new() { ["x"] = point.X, ["y"] = point.Y };

    // --- screenshots ---

    /// <summary>
    /// One screenshot into a temp directory that is removed once <paramref name="read"/> has taken what it needs from
    /// the response or the file, even when the caller abandons the capture.
    /// </summary>
    private Task<T> CaptureScreenshotAsync<T>(Func<RawScreenshotResult, string, Task<T>> read, bool normalizeStatusBar, CancellationToken cancellationToken) =>
        CommandAsync(
            "screenshot",
            async (client, token) =>
            {
                var directory = Directory.CreateTempSubdirectory("e2e-agent-device-");
                try
                {
                    var file = Path.Combine(directory.FullName, "screenshot.png");
                    var shot = await client.CallAsync(
                        DeviceMethods.CaptureScreenshot,
                        DeviceArgs.Of(("path", JsonValue.Create(file)), ("normalizeStatusBar", normalizeStatusBar ? JsonValue.Create(true) : null)),
                        token).ConfigureAwait(false);
                    return await read(
                        new RawScreenshotResult
                        {
                            Path = Text(shot?["path"]),
                            Width = Number(shot?["width"]),
                            Height = Number(shot?["height"]),
                            LogicalWidth = Number(shot?["logicalWidth"]),
                            LogicalHeight = Number(shot?["logicalHeight"]),
                            PixelDensity = Number(shot?["pixelDensity"]),
                        },
                        file).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        directory.Delete(recursive: true);
                    }
                    catch (IOException)
                    {
                        // The capture is best effort and so is its cleanup.
                    }
                }
            },
            cancellationToken);

    /// <summary>
    /// The viewport as a screenshot for comparison: the status bar in a fixed state, and every secure node on the screen
    /// painted over. Observes first so the regions describe the screen the pixels show. Throws when a secure field
    /// cannot be covered, because an image that may hold a credential must not leave the engine.
    /// </summary>
    internal async Task<EngineScreenshot> MaskedScreenshotAsync(CancellationToken cancellationToken)
    {
        var projected = Project(await SnapshotOrEmptyAsync(false, cancellationToken).ConfigureAwait(false));
        var data = await CaptureScreenshotAsync(async (shot, file) => await File.ReadAllBytesAsync(shot.Path ?? file, cancellationToken).ConfigureAwait(false), normalizeStatusBar: true, cancellationToken).ConfigureAwait(false);
        var viewport = projected.Viewport ?? _knownViewport;
        var masked = RedactSecure(data, projected, viewport)
            ?? throw new EngineException(EngineErrorCodes.EngineFailure, "a secure field on screen could not be masked; screenshot withheld", retryable: false);
        var scale = viewport is { Width: > 0 } known && DeviceSupport.ReadPngSize(masked) is { } size ? size.Width / known.Width : 1;
        return new EngineScreenshot(masked, scale);
    }

    /// <summary>
    /// Paints every secure node's bounds black on a screenshot of the same screen. Returns the masked bytes, or null
    /// when a secure node has no bounds or the image format cannot be edited: the caller then withholds the image
    /// rather than ship one it could not prove redacted. Bounds are in logical points. The image may be at device
    /// scale, so they are scaled by the image-to-viewport ratio.
    /// </summary>
    private static byte[]? RedactSecure(byte[] data, ProjectedSnapshot projected, ViewportSize? viewport)
    {
        var secure = projected.Index.Where(entry => entry.Node.States.Secure).ToList();
        if (secure.Count == 0)
        {
            return data;
        }

        if (DeviceSupport.ReadPngSize(data) is not { } size)
        {
            return null;
        }

        var scale = viewport is { Width: > 0 } known ? size.Width / known.Width : 1;
        var rects = new List<Rect>();
        foreach (var entry in secure)
        {
            if (entry.Node.Rect is not { } rect)
            {
                return null;
            }

            rects.Add(new Rect(rect.X * scale, rect.Y * scale, rect.Width * scale, rect.Height * scale));
        }

        try
        {
            return ScreenshotMasking.MaskPng(data, rects);
        }
        catch (Exception cause) when (cause is InvalidDataException or InvalidOperationException)
        {
            return null;
        }
    }

    internal static string Invariant(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
