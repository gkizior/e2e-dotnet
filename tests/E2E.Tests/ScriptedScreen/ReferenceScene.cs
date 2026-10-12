// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.ScriptedScreen;

/// <summary>
/// Upstream's reference screen for the deterministic tier: form controls, toggles, a list with nested
/// checkboxes, hidden twins, and the nodes that move while a test runs (a late arrival, a growing list,
/// a flipping state). The clocks start from a tap, not from the scene's creation, so a slow machine cannot
/// land a mutation before an assertion begins.
/// </summary>
internal static class ReferenceScene
{
    public static ScriptedScene Create(bool open = true)
    {
        var scene = new ScriptedScene(Build);
        if (open)
        {
            scene.Open();
        }

        return scene;
    }

    private static IEnumerable<ScriptedNode> Build(ScriptedScene stage)
    {
        var counter = new ScriptedNode { Id = "counter", Role = "status", Name = "Count", Text = "0" };
        var lateArrival = new ScriptedNode { Id = "late", Role = "button", Name = "Late arrival", Hidden = true };
        var loading = new ScriptedNode { Id = "loading", Role = "status", Name = "Loading", Text = "Loading" };
        var ticker = new ScriptedNode { Id = "ticker", Role = "status", Name = "Ticker", Text = "tick" };
        var progress = new ScriptedNode { Id = "progress", Role = "spinbutton", Name = "Progress", Value = "10" };
        var save = new ScriptedNode { Id = "save", Role = "menuitem", Name = "Save", Disabled = true };
        var growing = new ScriptedNode
        {
            Id = "growing",
            Role = "list",
            Name = "Growing",
            Children = [new ScriptedNode { Id = "grow-1", Role = "listitem", Text = "One" }],
        };

        var reveal = new ScriptedNode
        {
            Id = "reveal",
            Role = "button",
            Name = "Reveal",
            On = (action, _) =>
            {
                if (action is LocatorAction.Tap)
                {
                    stage.After(300, () =>
                    {
                        lateArrival.Hidden = false;
                        loading.Hidden = true;
                    });
                }
            },
        };
        var advance = new ScriptedNode
        {
            Id = "advance",
            Role = "button",
            Name = "Advance",
            On = (action, _) =>
            {
                if (action is LocatorAction.Tap)
                {
                    stage.After(250, () =>
                    {
                        ticker.Text = "tock";
                        progress.Value = "100";
                        save.Disabled = false;
                    });
                }
            },
        };
        var loadMore = new ScriptedNode
        {
            Id = "load-more",
            Role = "button",
            Name = "Load more",
            On = (action, _) =>
            {
                if (action is LocatorAction.Tap)
                {
                    stage.After(200, () => growing.Children.Add(new ScriptedNode { Id = "grow-2", Role = "listitem", Text = "Two" }));
                    stage.After(400, () => growing.Children.Add(new ScriptedNode { Id = "grow-3", Role = "listitem", Text = "Three" }));
                }
            },
        };

        static ScriptedNode Todo(int index, string text, bool done) => new()
        {
            Id = "todo-" + index,
            Role = "listitem",
            TestId = "todo",
            Text = text,
            Children = [new ScriptedNode { Id = "todo-" + index + "-done", Role = "checkbox", Name = "Done", Checked = done }],
        };

        return
        [
            new ScriptedNode { Id = "h1", Role = "heading", Name = "Dashboard", Text = "Dashboard", Level = 1 },
            new ScriptedNode { Id = "h2", Role = "heading", Name = "Recent", Text = "Recent", Level = 2 },
            new ScriptedNode
            {
                Id = "email",
                Role = "textbox",
                Name = "Email",
                Value = "",
                Attributes = new Dictionary<string, string> { ["placeholder"] = "you@example.test", ["autocomplete"] = "email" },
                Rect = new BoundingBox(10, 100, 200, 30),
            },
            new ScriptedNode { Id = "notes", Role = "textbox", Name = "Notes", Value = "line1\n\nline2  " },
            new ScriptedNode
            {
                Id = "password",
                Role = "textbox",
                Name = "Password",
                Value = "hunter2",
                InputPurpose = "password",
                Secure = true,
                Rect = new BoundingBox(10, 140, 200, 30),
            },
            new ScriptedNode { Id = "search", Role = "searchbox", Name = "Search", Value = "hello-value" },
            new ScriptedNode { Id = "notify", Role = "checkbox", Name = "Notifications" },
            new ScriptedNode { Id = "dark", Role = "switch", Name = "Dark mode", Checked = true },
            new ScriptedNode
            {
                Id = "plan",
                Role = "combobox",
                Name = "Plan",
                Value = "free",
                Options = [new ScriptedOption("Free", "free"), new ScriptedOption("Pro", "pro"), new ScriptedOption("Team", "team")],
            },
            new ScriptedNode { Id = "submit", Role = "button", Name = "Submit", Text = "Submit", Rect = new BoundingBox(20, 200, 100, 40) },
            new ScriptedNode { Id = "disabled", Role = "button", Name = "Disabled action", Disabled = true },
            new ScriptedNode
            {
                Id = "menu",
                Role = "button",
                Name = "Menu",
                On = (action, node) =>
                {
                    if (action is LocatorAction.Tap)
                    {
                        node.Expanded = !node.Expanded;
                    }
                },
            },
            new ScriptedNode
            {
                Id = "bold",
                Role = "button",
                Name = "Bold",
                On = (action, node) =>
                {
                    if (action is LocatorAction.Tap)
                    {
                        node.Pressed = true;
                    }
                },
            },
            new ScriptedNode
            {
                Id = "increment",
                Role = "button",
                Name = "Increment",
                On = (action, _) =>
                {
                    if (action is LocatorAction.Tap)
                    {
                        counter.Text = (int.Parse(counter.Text!, System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    }
                },
            },
            counter,
            new ScriptedNode { Id = "dup-1", Role = "button", Name = "Duplicated", Text = "Duplicated" },
            new ScriptedNode { Id = "dup-2", Role = "button", Name = "Duplicated", Text = "Duplicated" },
            new ScriptedNode { Id = "ready-hidden", Role = "status", Name = "Ready state", Text = "Ready", Hidden = true },
            new ScriptedNode { Id = "ready-visible", Role = "status", Name = "Ready state", Text = "Ready" },
            new ScriptedNode { Id = "hidden-text", Text = "Hidden content", Hidden = true },
            new ScriptedNode
            {
                Id = "todos",
                Role = "list",
                Name = "Todos",
                TestId = "todos",
                Children = [Todo(1, "Write spec", false), Todo(2, "Ship runner", true), Todo(3, "Release", false)],
            },
            new ScriptedNode
            {
                Id = "tabs",
                Role = "tablist",
                Name = "Filter",
                Children =
                [
                    new ScriptedNode { Id = "tab-all", Role = "tab", Name = "All", Text = "All", Selected = true },
                    new ScriptedNode { Id = "tab-open", Role = "tab", Name = "Open", Text = "Open" },
                ],
            },
            new ScriptedNode
            {
                Id = "card",
                Role = "region",
                Name = "Card",
                TestId = "card",
                Text = "Card body",
                Attributes = new Dictionary<string, string> { ["class"] = "card active", ["data-state"] = "open" },
            },
            new ScriptedNode { Id = "map", Role = "image", Name = "Map", Rect = new BoundingBox(100, 300, 400, 200) },
            new ScriptedNode { Id = "feed", Role = "list", Name = "Feed" },
            new ScriptedNode { Id = "focus-target", Role = "textbox", Name = "Focus target", Value = "" },
            new ScriptedNode { Id = "city", Role = "textbox", Name = "City", Value = "" },
            reveal,
            advance,
            loadMore,
            lateArrival,
            loading,
            ticker,
            progress,
            save,
            growing,
        ];
    }
}
