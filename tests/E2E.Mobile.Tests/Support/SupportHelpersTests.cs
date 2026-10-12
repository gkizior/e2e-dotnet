// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Mobile.Tests.Support;

public sealed class SupportHelpersTests
{
    [Fact]
    public void Reports_the_app_and_the_screen_title_as_a_location_that_is_not_a_URL()
    {
        Assert.Equal("com.apple.Preferences / General", DeviceSupport.ScreenLocation("com.apple.Preferences", "General"));
        Assert.Equal("com.apple.Preferences", DeviceSupport.ScreenLocation("com.apple.Preferences", null));
        Assert.Equal("General", DeviceSupport.ScreenLocation(null, "General"));
        Assert.Null(DeviceSupport.ScreenLocation(null, null));
        Assert.Equal("My App / Wi-Fi Settings", DeviceSupport.ScreenLocation("My App", " Wi-Fi  Settings "));

        // Two apps with a screen of the same name never share a location.
        Assert.NotEqual(DeviceSupport.ScreenLocation("com.apple.Preferences", "General"), DeviceSupport.ScreenLocation("com.example.other", "General"));

        // The harness applies its origin policy to anything that parses as a URL; a device location must not.
        Assert.False(Uri.TryCreate(DeviceSupport.ScreenLocation("com.apple.Preferences", "General"), UriKind.Absolute, out _));
    }

    [Fact]
    public void Swipes_against_the_scroll_direction_inside_the_rect()
    {
        var rect = new Rect(0, 100, 200, 400);
        Assert.Equal((new Point(100, 400), new Point(100, 200)), DeviceSupport.SwipeWithin(rect, ScrollDirection.Down, null));
        Assert.Equal((new Point(100, 140), new Point(100, 460)), DeviceSupport.SwipeWithin(rect, ScrollDirection.Up, Momentum.Fast));
        Assert.Equal((new Point(125, 300), new Point(75, 300)), DeviceSupport.SwipeWithin(rect, ScrollDirection.Right, Momentum.Slow));
        Assert.Equal((new Point(50, 300), new Point(150, 300)), DeviceSupport.SwipeWithin(rect, ScrollDirection.Left, null));
    }

    [Fact]
    public void Constrains_labels_to_safe_filenames()
    {
        Assert.Equal("after_checkout_1", DeviceSupport.SanitizeFilename("after checkout/1"));
        Assert.Equal("artifact", DeviceSupport.SanitizeFilename(""));
        Assert.Equal(64, DeviceSupport.SanitizeFilename(new string('x', 100)).Length);
    }
}
