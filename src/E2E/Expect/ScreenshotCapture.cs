// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Internal;

namespace E2E;

/// <summary>How a <c>toHaveScreenshot</c> call captures the screen or one locator's box on it.</summary>
internal static partial class ScreenshotAssertion
{
    /// <summary>One capture, or why there is none yet.</summary>
    private readonly record struct Capture(RgbaImage? Image, string? Missing);

    // --- capturing ---

    /// <summary>
    /// Captures for one call: the engine's viewport at one image pixel per viewport pixel, so a screenshot is the
    /// same size on every device density, cut to the subject's box when there is one, the masks painted over. The
    /// subject is scrolled into view once, the first time it is found; a subject that is not there yet is a capture
    /// still missing, so the call's poll keeps waiting and fails as an assertion. So is a capture the deadline cut short.
    /// </summary>
    private sealed class Capturer(Screen screen, ScreenshotContext store, Locator? subject, ScreenshotCall call, Deadline deadline)
    {
        private bool _scrolled = !store.ScrollsIntoView;

        public async Task<Capture> CaptureAsync(CancellationToken cancellationToken)
        {
            var remaining = deadline.Remaining(screen.Clock.GetUtcNow());
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            bounded.CancelAfter(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero);
            try
            {
                return await CaptureOnceAsync(bounded.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested && (bounded.IsCancellationRequested || deadline.Expired(screen.Clock.GetUtcNow()))
                && (ex is OperationCanceledException || ex is EngineException { Code: EngineErrorCodes.OperationTimeout or EngineErrorCodes.Cancelled } || ex.InnerException is OperationCanceledException))
            {
                return new Capture(null, "the last screenshot ran past the timeout");
            }
        }

        private async Task<Capture> CaptureOnceAsync(CancellationToken cancellationToken)
        {
            var shown = Subject(subject);
            var observation = await screen.ObserveAsync(cancellationToken).ConfigureAwait(false);
            BoundingBox? box = null;
            if (subject is not null)
            {
                var node = OneNode(observation);
                if (node is not null && !_scrolled)
                {
                    try
                    {
                        await screen.PerformAsync(node, new LocatorAction.ScrollIntoView(), cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is EngineException { Code: EngineErrorCodes.NodeStale } or TestException { Code: "NOT_FOUND" })
                    {
                    }

                    _scrolled = true;
                    observation = await screen.ObserveAsync(cancellationToken).ConfigureAwait(false);
                    node = OneNode(observation);
                }

                if (node is null)
                {
                    return new Capture(null, "no node matches " + shown);
                }

                if (node.States.Hidden || node.Rect is null)
                {
                    return new Capture(null, shown + " is not visible");
                }

                box = node.Rect;
            }

            var shot = await store.Capture(cancellationToken).ConfigureAwait(false);
            var raw = ImageOps.DecodePng(shot.Png);
            var scale = shot.Scale > 0 ? shot.Scale : 1;
            var image = ImageOps.ResampleImage(raw, Math.Max(1, (int)Math.Round(raw.Width / scale)), Math.Max(1, (int)Math.Round(raw.Height / scale)));

            // The engine covers secure fields in the pixels; painting the observed ones as well covers a field the engine's selector missed.
            var secure = LocatorResolver.Walk(observation.Roots).Where(node => node.States.Secure && !node.States.Hidden && node.Rect is not null).Select(node => node.Rect!);
            image = ImageOps.FillBoxes(image, secure, (0, 0, 0));
            if (call.Masks.Count > 0)
            {
                var masks = call.Masks.SelectMany(mask => LocatorResolver.Resolve(observation, mask.Query)).Where(node => !node.States.Hidden && node.Rect is not null).Select(node => node.Rect!);
                image = ImageOps.FillBoxes(image, masks, call.MaskColor);
            }

            if (box is null)
            {
                return new Capture(image, null);
            }

            return ImageOps.ClipBox(image.Width, image.Height, box) is { } clipped
                ? new Capture(ImageOps.CropImage(image, clipped), null)
                : new Capture(null, shown + " is outside the viewport");
        }

        private SemanticNode? OneNode(Observation observation)
        {
            var matches = LocatorResolver.Resolve(observation, subject!.Query);
            return matches.Count switch
            {
                0 => null,
                1 => matches[0],
                _ => throw new TestException("STRICT_MODE", subject.Query.Describe() + " matched " + Number(matches.Count) + " nodes."),
            };
        }
    }
}
