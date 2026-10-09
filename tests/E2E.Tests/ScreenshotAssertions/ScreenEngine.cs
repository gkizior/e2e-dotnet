// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Internal;

namespace E2E.Tests.ScreenshotAssertions;

/// <summary>
/// An engine that shows a screen the test chooses: a white viewport with one button drawn in another color.
/// The tree lists the button and a clock, so locators resolve and have boxes to cut and mask.
/// </summary>
internal sealed class ScreenEngine : IEngine
{
    public const int Width = 1280;
    public const int Height = 720;
    public static readonly BoundingBox Button = new(100, 50, 40, 20);

    public static readonly (byte R, byte G, byte B) White = (255, 255, 255);
    public static readonly (byte R, byte G, byte B) Blue = (0, 0, 255);
    public static readonly (byte R, byte G, byte B) Red = (255, 0, 0);

    /// <summary>The screen of the next capture.</summary>
    public Func<EngineScreenshot> Current { get; set; } = () => Shot(White, Blue);

    /// <summary>Runs before every capture; a wait that ignores cancellation, or a throw.</summary>
    public Func<Task>? BeforeCapture { get; set; }

    public bool WithSecureField { get; init; }

    public int Captures { get; private set; }

    public string Platform => "fake";

    public string Version => "0";

    public EngineCapabilities Capabilities =>
        EngineCapabilities.Observation | EngineCapabilities.Actions | EngineCapabilities.Location | EngineCapabilities.Scroll | EngineCapabilities.Screenshot;

    public Task<IEngineSession> StartAsync(EngineStartOptions options, CancellationToken cancellationToken) => Task.FromResult<IEngineSession>(new Session(this));

    /// <summary>A screen of one background color with the button drawn in another, <paramref name="scale"/> image pixels per viewport pixel.</summary>
    public static EngineScreenshot Shot((byte R, byte G, byte B) background, (byte R, byte G, byte B) button, int scale = 1)
    {
        var width = Width * scale;
        var height = Height * scale;
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var inButton = x >= Button.X * scale && x < (Button.X + Button.Width) * scale && y >= Button.Y * scale && y < (Button.Y + Button.Height) * scale;
                var (r, g, b) = inButton ? button : background;
                var at = ((y * width) + x) * 4;
                data[at] = r;
                data[at + 1] = g;
                data[at + 2] = b;
                data[at + 3] = 255;
            }
        }

        return new EngineScreenshot(ImageOps.EncodePng(new RgbaImage(width, height, data)), scale);
    }

    private sealed class Session(ScreenEngine engine) : IEngineSession
    {
        public string Route => "/";

        public Task OpenAsync(string url, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<Observation> ObserveAsync(CancellationToken cancellationToken)
        {
            List<SemanticNode> children =
            [
                new SemanticNode { Ref = "button", Role = "button", Name = "Submit", Rect = Button },
                new SemanticNode { Ref = "clock", Role = "text", Name = "Clock", TestId = "clock", Rect = new BoundingBox(300, 10, 50, 10) },
            ];
            if (engine.WithSecureField)
            {
                children.Add(new SemanticNode { Ref = "password", Role = "textbox", Name = "Password", States = new NodeStates { Secure = true }, Rect = new BoundingBox(500, 100, 60, 20) });
            }

            return Task.FromResult(new Observation { Route = "/", Roots = [new SemanticNode { Ref = "root", Role = "document", Children = children }] });
        }

        public Task PerformAsync(SemanticNode node, LocatorAction action, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task PressAsync(string key, CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task<EngineScreenshot> ScreenshotAsync(CancellationToken cancellationToken)
        {
            engine.Captures++;
            if (engine.BeforeCapture is { } before)
            {
                await before().ConfigureAwait(false);
            }

            return engine.Current();
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
