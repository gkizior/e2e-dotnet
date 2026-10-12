// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using static E2E.Mobile.Tests.RawNodes;

namespace E2E.Mobile.Tests.Nodes;

public sealed class SnapshotProjectionTests
{
    private const string SystemUi = "com.android.systemui";

    private static List<string?> ChildNames(SemanticNode node) => [.. node.Children.Select(child => child.Name)];

    private static List<string?> Roles(ProjectedSnapshot projected) => [.. projected.Index.Select(entry => entry.Node.Role)];

    private static List<string?> Names(ProjectedSnapshot projected) => [.. projected.Index.Select(entry => entry.Node.Name)];

    [Fact]
    public void Rebuilds_the_tree_from_parentIndex_and_keeps_document_order_in_the_index()
    {
        var projected = Project(SettingsNodes);
        var root = Assert.Single(projected.Roots);
        Assert.Equal("Settings", root.Name);
        Assert.Equal(["General", "About", "Airplane Mode", "Search", "Password", "Hidden", "Scroller"], ChildNames(root));
        Assert.Equal("Back", root.Children[0].Children[0].Name);
        Assert.Equal(SettingsNodes.Select((_, position) => $"n{position + 1}"), projected.Index.Select(entry => entry.Id));
        Assert.Equal("navigation-bar", projected.Index[2].Parent?.Kind);
    }

    [Fact]
    public void Falls_back_to_depth_nesting_when_parentIndex_is_absent_and_to_flat_roots_without_either()
    {
        var byDepth = Project(
            N("@e1", depth: 0, type: "window"),
            N("@e2", depth: 1, type: "button", label: "A"),
            N("@e3", depth: 2, type: "static-text", label: "inner"),
            N("@e4", depth: 1, type: "button", label: "B"));
        var window = Assert.Single(byDepth.Roots);
        Assert.Equal(["A", "B"], ChildNames(window));
        Assert.Equal("inner", window.Children[0].Children[0].Name);

        var flat = Project(N("@e1", type: "button"), N("@e2", type: "button"));
        Assert.Equal(2, flat.Roots.Count);
    }

    [Fact]
    public void Derives_states_drops_secure_values_and_exposes_identifiers_as_the_test_id_attribute()
    {
        var index = Project(SettingsNodes).Index;
        ProjectedNode ByName(string name) => index.First(entry => entry.Node.Name == name);

        Assert.Equal(S(("checked", false)), ByName("Airplane Mode").States);
        Assert.Equal("About", ByName("About").Node.Text);
        Assert.Equal("wifi", ByName("Search").Node.Value);
        var secure = ByName("Password");
        Assert.Null(secure.Node.Value);
        Assert.Equal(S(("secure", true)), secure.States);
        Assert.Equal("password", secure.Node.InputPurpose);
        Assert.Equal(S(("disabled", true), ("hidden", true)), ByName("Hidden").States);
        Assert.Equal("ABOUT", ByName("About").Node.TestId);
        Assert.Null(ByName("About").Node.Placeholder);
        Assert.Equal("id=ABOUT", ByName("About").Selector);
        Assert.Null(ByName("Search").Selector);
        Assert.Equal(new BoundingBox(0, 47, 390, 44), ByName("Back").Node.Rect);
    }

    [Fact]
    public void Reports_the_accessibility_identifier_as_the_test_id_and_quotes_identifiers_with_spaces_in_the_selector()
    {
        var projected = SnapshotProjection.ProjectSnapshot([N("@e1", type: "button", identifier: "save button")], () => "n1");
        Assert.Equal("save button", projected.Roots[0].TestId);
        Assert.Equal("id=\"save button\"", projected.Index[0].Selector);
    }

    [Fact]
    public void Strips_the_at_prefix_off_refs_and_leaves_a_node_without_a_ref_unactionable()
    {
        var index = Project(N("@e7", type: "button"), N("e8", type: "button"), N(type: "static-text", label: "x")).Index;
        Assert.Equal(["e7", "e8", ""], index.Select(entry => entry.Ref));
    }

    [Fact]
    public void Reads_the_viewport_off_the_application_node_else_off_the_rect_extent()
    {
        Assert.Equal(new ViewportSize(390, 844), SnapshotProjection.ViewportOf(SettingsNodes));
        Assert.Equal(new ViewportSize(110, 70), SnapshotProjection.ViewportOf([N(type: "button", rect: R(10, 20, 100, 50))]));
        Assert.Null(SnapshotProjection.ViewportOf([N(type: "button")]));
    }

    [Fact]
    public void Measures_system_bars_against_a_published_viewport_where_the_tree_alone_would_end_the_screen_at_its_lowest_window()
    {
        // A panel above the bottom of a 2400 px screen, at the bottom of a tree that reaches only 1800 px.
        RawNode[] nodes =
        [
            N("@e1", depth: 0, type: "android.widget.FrameLayout", bundleId: "dev.e2e.benchmark", rect: R(0, 0, 1080, 1800)),
            N("@e2", depth: 0, type: "android.widget.FrameLayout", bundleId: SystemUi, rect: R(0, 1700, 1080, 100)),
        ];
        var published = SnapshotProjection.ProjectSnapshot(nodes, () => "n", new ViewportSize(1080, 2400));
        Assert.Equal(new ViewportSize(1080, 2400), published.Viewport);
        Assert.Equal(2, published.Roots.Count);
        var inferred = SnapshotProjection.ProjectSnapshot(nodes, () => "n");
        Assert.Equal(new ViewportSize(1080, 1800), inferred.Viewport);
        Assert.Single(inferred.Roots);
    }

    [Fact]
    public void Leaves_the_Android_status_bar_and_navigation_bar_out_with_their_children_and_keeps_the_app_window()
    {
        var projected = Project(
            N("@e1", depth: 0, type: "androidx.compose.ui.platform.ComposeView", bundleId: SystemUi, rect: R(0, 0, 1080, 63)),
            N("@e2", depth: 1, type: "android.widget.TextView", bundleId: SystemUi, label: "10:17", identifier: "com.android.systemui:id/clock", rect: R(11, 2, 136, 58)),
            N("@e3", depth: 1, type: "android.widget.FrameLayout", bundleId: SystemUi, label: "T-Mobile, three bars.", identifier: "com.android.systemui:id/mobile_combo", rect: R(797, 2, 61, 58)),
            N("@e4", depth: 0, type: "android.widget.FrameLayout", bundleId: "dev.e2e.benchmark", rect: R(0, 0, 1080, 2400)),
            N("@e5", depth: 1, type: "android.widget.TextView", bundleId: "dev.e2e.benchmark", label: "Hello", rect: R(0, 200, 1080, 60)),
            N("@e6", depth: 0, type: "android.widget.FrameLayout", bundleId: SystemUi, rect: R(0, 2274, 1080, 126)),
            N("@e7", depth: 1, type: "android.widget.ImageView", bundleId: SystemUi, label: "Back", rect: R(100, 2290, 100, 100)));
        var window = Assert.Single(projected.Roots);
        Assert.Equal(["Hello"], ChildNames(window));
        Assert.Equal([null, "Hello"], Names(projected));
        Assert.Equal(new ViewportSize(1080, 2400), projected.Viewport);
    }

    [Fact]
    public void Leaves_out_status_icons_agent_device_reports_as_top_level_nodes_of_their_own_and_keeps_a_wide_popup_and_a_narrow_one_with_a_control_in_the_same_band()
    {
        var projected = Project(
            N("@e1", type: "android.widget.TextView", bundleId: SystemUi, label: "11:25", identifier: "com.android.systemui:id/clock", rect: R(47, 47, 125, 43)),
            N("@e2", type: "android.widget.ImageView", bundleId: SystemUi, label: "Android System notification: ", rect: R(172, 1, 58, 135)),
            N("@e3", type: "android.widget.FrameLayout", bundleId: SystemUi, label: "T-Mobile, two bars.", identifier: "com.android.systemui:id/mobile_combo", rect: R(930, 39, 39, 58)),
            N("@e4", type: "android.widget.FrameLayout", bundleId: SystemUi, label: "Download complete", rect: R(40, 20, 1000, 150)),
            N("@e5", index: 5, depth: 0, type: "android.widget.FrameLayout", bundleId: SystemUi, label: "Screen recording", rect: R(80, 0, 400, 140)),
            N("@e6", index: 6, depth: 1, parentIndex: 5, type: "android.widget.Button", bundleId: SystemUi, label: "Stop", rect: R(300, 40, 120, 60)),
            N("@e7", depth: 0, type: "android.widget.FrameLayout", bundleId: "dev.e2e.benchmark", rect: R(0, 0, 1080, 2400)));
        Assert.Equal(["Download complete", "Screen recording", null], projected.Roots.Select(root => root.Name));
        Assert.Contains("Stop", Names(projected));
    }

    [Fact]
    public void Keeps_a_systemui_window_that_covers_the_screen_and_every_window_on_a_platform_that_reports_no_package()
    {
        var shade = Project(
            N("@e1", depth: 0, type: "android.widget.FrameLayout", bundleId: "dev.e2e.benchmark", rect: R(0, 0, 1080, 2400)),
            N("@e2", depth: 0, type: "android.widget.FrameLayout", bundleId: "com.android.systemui", rect: R(0, 0, 1080, 2400)),
            N("@e3", depth: 1, type: "android.widget.TextView", bundleId: "com.android.systemui", label: "No notifications", rect: R(0, 1000, 1080, 60)));
        Assert.Equal(2, shade.Roots.Count);
        Assert.Contains("No notifications", Names(shade));

        var ios = Project(
            N("@e1", depth: 0, type: "Window", rect: R(0, 0, 390, 844)),
            N("@e2", depth: 0, type: "Window", rect: R(0, 0, 390, 47)),
            N("@e3", depth: 1, type: "StaticText", label: "9:41", rect: R(20, 10, 60, 20)));
        Assert.Equal(2, ios.Roots.Count);
    }

    [Fact]
    public void Keeps_a_compact_systemui_popup_at_an_edge_controls_included_while_the_full_width_bar_beside_it_goes()
    {
        var projected = Project(
            N("@e1", depth: 0, type: "androidx.compose.ui.platform.ComposeView", bundleId: SystemUi, rect: R(0, 0, 1080, 63)),
            N("@e2", depth: 1, type: "android.widget.TextView", bundleId: SystemUi, label: "10:17", rect: R(11, 2, 136, 58)),
            N("@e3", depth: 0, type: "android.widget.FrameLayout", bundleId: SystemUi, rect: R(80, 0, 920, 180)),
            N("@e4", depth: 1, type: "android.widget.Button", bundleId: SystemUi, label: "Reply", rect: R(700, 100, 200, 60)),
            N("@e5", depth: 0, type: "android.widget.FrameLayout", bundleId: "dev.e2e.benchmark", rect: R(0, 0, 1080, 2400)),
            N("@e6", depth: 1, type: "android.widget.TextView", bundleId: "dev.e2e.benchmark", label: "Hello", rect: R(0, 200, 1080, 60)));
        Assert.Equal(2, projected.Roots.Count);
        Assert.Contains("Reply", Names(projected));
        Assert.DoesNotContain("10:17", Names(projected));
    }

    [Fact]
    public void Reads_the_screen_off_the_largest_window_so_a_bar_that_is_a_window_itself_still_goes()
    {
        var projected = Project(
            N("@e1", depth: 0, type: "Window", bundleId: SystemUi, rect: R(0, 0, 1080, 63)),
            N("@e2", depth: 1, type: "android.widget.TextView", bundleId: SystemUi, label: "10:17", rect: R(11, 2, 136, 58)),
            N("@e3", depth: 0, type: "Window", bundleId: "dev.e2e.benchmark", rect: R(0, 0, 1080, 2400)),
            N("@e4", depth: 1, type: "android.widget.TextView", bundleId: "dev.e2e.benchmark", label: "Hello", rect: R(0, 200, 1080, 60)));
        Assert.Equal(new ViewportSize(1080, 2400), projected.Viewport);
        var window = Assert.Single(projected.Roots);
        Assert.Equal(["Hello"], ChildNames(window));
    }

    [Fact]
    public void Normalizes_XCTest_PascalCase_element_types_onto_the_same_vocabulary()
    {
        var projected = Project(
            N("e1", index: 0, depth: 0, type: "Application", label: "Settings", rect: R(0, 0, 390, 844)),
            N("e2", index: 1, parentIndex: 0, depth: 1, type: "NavigationBar", identifier: "Settings"),
            N("e3", index: 2, parentIndex: 1, depth: 2, type: "StaticText", label: "Settings"),
            N("e4", index: 3, parentIndex: 0, depth: 1, type: "CollectionView"),
            N("e5", index: 4, parentIndex: 3, depth: 2, type: "Cell"),
            N("e6", index: 5, parentIndex: 4, depth: 3, type: "Button", label: "General", identifier: "com.apple.settings.general"),
            N("e7", index: 6, parentIndex: 0, depth: 1, type: "SecureTextField", label: "Passcode", value: "1234"));
        Assert.Equal(
            ["application", "navigation-bar", "static-text", "collection-view", "cell", "button", "secure-text-field"],
            projected.Index.Select(entry => entry.Kind));
        Assert.Equal(["application", "navigation", "text", "collection-view", "listitem", "button", "textbox"], Roles(projected));
        Assert.Equal(S(("secure", true)), projected.Index[6].States);
        Assert.Equal(new ViewportSize(390, 844), projected.Viewport);
        Assert.Equal("Settings", SnapshotProjection.ScreenTitle(projected));
    }

    [Fact]
    public void Maps_iOS_composite_widgets_onto_the_vocabulary_roles_a_browser_reports_for_them()
    {
        var projected = Project(
            N("e1", index: 0, depth: 0, type: "Application", label: "Shop"),
            N("e2", index: 1, parentIndex: 0, depth: 1, type: "TabBar"),
            N("e3", index: 2, parentIndex: 1, depth: 2, type: "Button", label: "Cart", selected: true),
            N("e4", index: 3, parentIndex: 0, depth: 1, type: "SegmentedControl"),
            N("e5", index: 4, parentIndex: 3, depth: 2, type: "Button", label: "Weekly"),
            N("e6", index: 5, parentIndex: 0, depth: 1, type: "ProgressIndicator", label: "Upload", value: "40%"),
            N("e7", index: 6, parentIndex: 0, depth: 1, type: "ActivityIndicator", label: "Loading"),
            N("e8", index: 7, parentIndex: 0, depth: 1, type: "Stepper", label: "Quantity", value: "2"),
            N("e9", index: 8, parentIndex: 0, depth: 1, type: "Toolbar"),
            N("e10", index: 9, parentIndex: 0, depth: 1, type: "Menu"),
            N("e11", index: 10, parentIndex: 9, depth: 2, type: "MenuItem", label: "Copy"),
            N("e12", index: 11, parentIndex: 0, depth: 1, type: "RadioGroup"),
            N("e13", index: 12, parentIndex: 11, depth: 2, type: "RadioButton", label: "Card"),
            N("e14", index: 13, parentIndex: 0, depth: 1, type: "Table"),
            N("e15", index: 14, parentIndex: 13, depth: 2, type: "TableRow"),
            N("e16", index: 15, parentIndex: 0, depth: 1, type: "Group"),
            N("e17", index: 16, parentIndex: 0, depth: 1, type: "Other"),
            N("e18", index: 17, parentIndex: 0, depth: 1, type: "PickerWheel"));
        Assert.Equal(
            [
                "application",
                "tablist",
                "tab",
                "tablist",
                "tab",
                "progressbar",
                "status",
                "spinbutton",
                "toolbar",
                "menu",
                "menuitem",
                "radiogroup",
                "radio",
                "table",
                "row",
                "group",
                "other",
                "picker-wheel",
            ],
            Roles(projected));
        Assert.Equal("40%", projected.Index[5].Node.Value);
        Assert.Equal(S(("selected", true)), projected.Index[2].States);

        // Only a tab container's buttons are tabs; the toolbar's stay buttons.
        var toolbar = Project(
            N("e1", index: 0, depth: 0, type: "Toolbar"),
            N("e2", index: 1, parentIndex: 0, depth: 1, type: "Button", label: "Share"));
        Assert.Equal(["toolbar", "button"], Roles(toolbar));
    }

    [Fact]
    public void Maps_Android_composite_widgets_including_the_nested_tab_and_bottom_navigation_item_classes()
    {
        var projected = Project(
            N("e1", index: 0, depth: 0, type: "android.widget.FrameLayout"),
            N("e2", index: 1, parentIndex: 0, depth: 1, type: "com.google.android.material.tabs.TabLayout"),
            N("e3", index: 2, parentIndex: 1, depth: 2, type: "com.google.android.material.tabs.TabLayout$TabView", label: "Open"),
            N("e4", index: 3, parentIndex: 0, depth: 1, type: "com.google.android.material.bottomnavigation.BottomNavigationView"),
            N("e5", index: 4, parentIndex: 3, depth: 2, type: "com.google.android.material.bottomnavigation.BottomNavigationItemView", label: "Home"),
            N("e6", index: 5, parentIndex: 0, depth: 1, type: "android.widget.ProgressBar", label: "Upload"),
            N("e7", index: 6, parentIndex: 0, depth: 1, type: "android.widget.RadioGroup"),
            N("e8", index: 7, parentIndex: 6, depth: 2, type: "android.widget.RadioButton", label: "Card"),
            N("e9", index: 8, parentIndex: 0, depth: 1, type: "androidx.appcompat.widget.Toolbar"),
            N("e10", index: 9, parentIndex: 0, depth: 1, type: "android.widget.NumberPicker"),
            N("e11", index: 10, parentIndex: 0, depth: 1, type: "android.widget.TabWidget"),
            N("e12", index: 11, parentIndex: 0, depth: 1, type: "android.widget.AbsListView", identifier: "fruit-list"));
        Assert.Equal(
            [
                "frame-layout",
                "tab-layout",
                "tab-layout$tab-view",
                "bottom-navigation-view",
                "bottom-navigation-item-view",
                "progress-bar",
                "radio-group",
                "radio-button",
                "toolbar",
                "number-picker",
                "tab-widget",
                "abs-list-view",
            ],
            projected.Index.Select(entry => entry.Kind));
        Assert.Equal(
            ["group", "tablist", "tab", "tablist", "tab", "progressbar", "radiogroup", "radio", "toolbar", "spinbutton", "tablist", "list"],
            Roles(projected));
    }

    [Fact]
    public void Names_an_Android_node_by_its_content_description_keeps_its_text_and_reads_heading_role_description_checked_and_placeholder()
    {
        var projected = Project(
            N("e1", index: 0, depth: 0, type: "android.widget.FrameLayout"),
            N("e2", index: 1, parentIndex: 0, depth: 1, type: "android.widget.TextView", label: "Plain controls, every state exposed", contentDescription: "Inventory heading", heading: true, identifier: "inventory-header"),
            N("e3", index: 2, parentIndex: 0, depth: 1, type: "android.view.View", roleDescription: "Tab List"),
            N("e4", index: 3, parentIndex: 2, depth: 2, type: "android.view.View", label: "Fields", roleDescription: "Tab", selected: true),
            N("e5", index: 4, parentIndex: 0, depth: 1, type: "android.widget.EditText", label: "Ada Lovelace", value: "Ada Lovelace", contentDescription: "Name field", placeholder: "Type your name", editable: true),
            N("e6", index: 5, parentIndex: 0, depth: 1, type: "android.widget.EditText", label: "Type your name", value: "Type your name", contentDescription: "Name field", placeholder: "Type your name", hintShowing: true, editable: true),
            N("e7", index: 6, parentIndex: 0, depth: 1, type: "android.widget.Switch", label: "Wi-Fi switch", @checked: false),
            N("e8", index: 7, parentIndex: 0, depth: 1, type: "android.widget.RadioButton", label: "Medium", @checked: true),
            N("e9", index: 8, parentIndex: 0, depth: 1, type: "android.view.View", label: "Plain", roleDescription: "Link"),
            N("e10", index: 9, parentIndex: 0, depth: 1, type: "android.widget.TextView", label: "Total", contentDescription: ""));
        var entries = projected.Index;
        var (header, tablist, tab, filled, empty, toggle, radio, plain, total) = (entries[1], entries[2], entries[3], entries[4], entries[5], entries[6], entries[7], entries[8], entries[9]);

        Assert.Equal("heading", header.Node.Role);
        Assert.Equal("Inventory heading", header.Node.Name);
        Assert.Equal("Plain controls, every state exposed", header.Node.Text);
        Assert.Equal("inventory-header", header.Node.TestId);
        Assert.Equal("tablist", tablist.Node.Role);
        Assert.Equal("tab", tab.Node.Role);
        Assert.Equal("Fields", tab.Node.Name);
        Assert.Equal("Fields", tab.Node.Text);
        Assert.True(tab.Node.States.Selected);
        Assert.Equal("textbox", filled.Node.Role);
        Assert.Equal("Name field", filled.Node.Name);
        Assert.Equal("Ada Lovelace", filled.Node.Text);
        Assert.Equal("Ada Lovelace", filled.Node.Value);
        Assert.Equal("Type your name", filled.Node.Placeholder);

        // An empty field shows its hint as its text; the hint flag says it holds nothing.
        Assert.Equal("textbox", empty.Node.Role);
        Assert.Equal("Name field", empty.Node.Name);
        Assert.Equal("Type your name", empty.Node.Text);
        Assert.Equal("Type your name", empty.Node.Placeholder);
        Assert.Null(empty.Node.Value);
        Assert.Equal("switch", toggle.Node.Role);
        Assert.Equal("Wi-Fi switch", toggle.Node.Name);
        Assert.Equal(S(("checked", false)), toggle.States);
        Assert.Equal("radio", radio.Node.Role);
        Assert.Equal(S(("checked", true)), radio.States);

        // A role description outside the vocabulary leaves the class role; an empty description leaves the label as the name.
        Assert.Equal("group", plain.Node.Role);
        Assert.Equal("Plain", plain.Node.Name);
        Assert.Equal("text", total.Node.Role);
        Assert.Equal("Total", total.Node.Name);
        Assert.Equal("Total", total.Node.Text);
        Assert.Null(total.Node.Placeholder);
    }

    [Fact]
    public void Reads_the_role_and_state_React_Native_spells_into_an_iOS_accessibility_value_and_reports_the_rest_as_the_value()
    {
        var projected = Project(
            N("e1", index: 0, depth: 0, type: "Application", label: "Benchmark"),
            N("e2", index: 1, parentIndex: 0, depth: 1, type: "Other", label: "Newsletter", value: "checkbox, unchecked", identifier: "newsletter-checkbox"),
            N("e3", index: 2, parentIndex: 0, depth: 1, type: "Other", label: "Newsletter", value: "checkbox, checked"),
            N("e4", index: 3, parentIndex: 0, depth: 1, type: "Other", label: "Small", value: "radio button, checked", selected: true),
            N("e5", index: 4, parentIndex: 0, depth: 1, type: "Other", label: "Medium", value: "radio button, unchecked"),
            N("e6", index: 5, parentIndex: 0, depth: 1, type: "Other", label: "Terms", value: "checkbox, mixed"),
            N("e7", index: 6, parentIndex: 0, depth: 1, type: "Other", label: "Filters", value: "menu, expanded"),
            N("e8", index: 7, parentIndex: 0, depth: 1, type: "Other", label: "More", value: "menu, collapsed, busy"),
            N("e9", index: 8, parentIndex: 0, depth: 1, type: "Other", label: "Volume", value: "spin button, 40%"),
            N("e10", index: 9, parentIndex: 0, depth: 1, type: "Other", label: "Home", value: "tab", selected: true),
            N("e11", index: 10, parentIndex: 0, depth: 1, type: "Other", label: "Upload", value: "progress bar, 3 of 5, almost done"),
            N("e12", index: 11, parentIndex: 0, depth: 1, type: "Other", label: "Note", value: "checkboxes are fun"),
            N("e13", index: 12, parentIndex: 0, depth: 1, type: "Switch", label: "Wi-Fi", value: "1"),
            N("e14", index: 13, parentIndex: 0, depth: 1, type: "Other", label: "Tabs", value: "tab list"));
        var nodes = projected.Index.Skip(1).ToList();
        Assert.Equal(
            ["checkbox", "checkbox", "radio", "radio", "checkbox", "menu", "menu", "spinbutton", "tab", "progressbar", "other", "switch", "tablist"],
            nodes.Select(entry => entry.Node.Role));
        Assert.Equal(
            [
                S(("checked", false)),
                S(("checked", true)),
                S(("selected", true), ("checked", true)),
                S(("checked", false)),
                S(),
                S(("expanded", true)),
                S(("expanded", false)),
                S(),
                S(("selected", true)),
                S(),
                S(),
                S(("checked", true)),
                S(),
            ],
            nodes.Select(entry => entry.States));

        // The descriptor and state words are stripped; a value the app set, and a state the contract has no field for, stay.
        Assert.Equal(
            [null, null, null, null, "mixed", null, "busy", "40%", null, "3 of 5, almost done", "checkboxes are fun", "1", null],
            nodes.Select(entry => entry.Node.Value));
        Assert.Equal("Newsletter", nodes[0].Node.Name);
        Assert.Equal("newsletter-checkbox", nodes[0].Node.TestId);
        Assert.Equal("id=newsletter-checkbox", nodes[0].Selector);

        // The platform kind is kept for selectors: `role=Other` still finds the view.
        Assert.Equal("other", projected.Index[1].Kind);

        // Android never writes this shape; a value that happens to start with a word from the list is a value.
        var android = Project(N("e1", type: "android.widget.TextView", label: "Kind", value: "checkbox, unchecked"));
        Assert.Equal("text", android.Index[0].Node.Role);
        Assert.Equal("checkbox, unchecked", android.Index[0].Node.Value);

        // A native iOS control's value is content, whatever words it starts with: only an `Other` carries the encoding.
        var field = Project(N("e1", type: "TextField", label: "Kind", value: "checkbox, unchecked"));
        Assert.Equal("textbox", field.Index[0].Node.Role);
        Assert.Equal("checkbox, unchecked", field.Index[0].Node.Value);
        Assert.DoesNotContain("checked", field.Index[0].States.Keys);

        // An empty field shows its placeholder, which XCTest reports as the value; the hint flag says it is empty.
        var empty = Project(N("e1", type: "TextField", label: "Name field", value: "Type your name", hintShowing: true));
        Assert.Equal("textbox", empty.Index[0].Node.Role);
        Assert.Null(empty.Index[0].Node.Value);
        Assert.Empty(android.Index[0].States);
    }

    [Fact]
    public void Keeps_the_value_of_an_iOS_field_whose_text_is_also_its_label_and_drops_only_the_Android_text_view_echo()
    {
        var projected = Project(
            N("e1", index: 0, depth: 0, type: "Application", label: "Benchmark"),
            N("e2", index: 1, parentIndex: 0, depth: 1, type: "TextField", label: "tester", value: "tester", identifier: "username-input"),
            N("e3", index: 2, parentIndex: 0, depth: 1, type: "TextView", label: "Notes long enough", value: "Notes long enough"),
            N("e4", index: 3, parentIndex: 0, depth: 1, type: "StaticText", label: "Row 1", value: "Row 1"),
            N("e5", index: 4, parentIndex: 0, depth: 1, type: "android.widget.TextView", label: "Airplane mode", value: "Airplane mode"),
            N("e6", index: 5, parentIndex: 0, depth: 1, type: "android.widget.EditText", label: "tester", value: "tester"),
            N("e7", index: 6, parentIndex: 0, depth: 1, type: "android.widget.TextView", label: "PIN", value: "PIN", editable: true),
            N("e8", index: 7, parentIndex: 0, depth: 1, type: "android.widget.TextView", label: "Total", value: "42"));
        Assert.Equal(
            [("textbox", "tester"), ("textbox", "Notes long enough"), ("text", "Row 1"), ("text", null), ("textbox", "tester"), ("textbox", "PIN"), ("text", "42")],
            projected.Index.Skip(1).Select(entry => (entry.Node.Role!, entry.Node.Value)));
    }

    [Fact]
    public void Maps_Android_view_classes_drops_echoed_values_and_titles_the_screen_from_the_toolbar()
    {
        var projected = Project(
            N("e1", index: 0, depth: 0, type: "android.widget.FrameLayout"),
            N("e2", index: 1, parentIndex: 0, depth: 1, type: "android.widget.FrameLayout", label: "Network & internet", identifier: "com.android.settings:id/collapsing_toolbar"),
            N("e3", index: 2, parentIndex: 0, depth: 1, type: "androidx.recyclerview.widget.RecyclerView", identifier: "com.android.settings:id/recycler_view"),
            N("e4", index: 3, parentIndex: 2, depth: 2, type: "android.widget.LinearLayout"),
            N("e5", index: 4, parentIndex: 3, depth: 3, type: "android.widget.TextView", label: "Airplane mode", value: "Airplane mode", identifier: "android:id/title"),
            N("e6", index: 5, parentIndex: 3, depth: 3, type: "android.widget.Switch"),
            N("e7", index: 6, parentIndex: 2, depth: 2, type: "android.widget.EditText", label: "Search", value: "wifi"),
            N("e8", index: 7, parentIndex: 2, depth: 2, type: "android.widget.ImageButton", label: "Back"));
        Assert.Equal(
            ["frame-layout", "frame-layout", "recycler-view", "linear-layout", "text-view", "switch", "edit-text", "image-button"],
            projected.Index.Select(entry => entry.Kind));
        Assert.Equal(["group", "group", "list", "group", "text", "switch", "textbox", "button"], Roles(projected));
        var title = projected.Index[4].Node;
        Assert.Equal("Airplane mode", title.Name);
        Assert.Null(title.Value);
        Assert.Equal("wifi", projected.Index[6].Node.Value);
        Assert.Equal("android:id/title", title.TestId);
        Assert.Equal("Network & internet", SnapshotProjection.ScreenTitle(projected));
        Assert.Null(SnapshotProjection.ScreenTitle(Project(N("e1", type: "android.widget.FrameLayout", label: "x"))));
    }

    [Fact]
    public void Marks_an_Android_password_EditText_secure_by_attribute_and_leaves_a_plain_EditText_its_value()
    {
        var projected = Project(
            N("e1", index: 0, depth: 0, type: "android.widget.FrameLayout", rect: R(0, 0, 390, 844)),
            N("e2", index: 1, parentIndex: 0, depth: 1, type: "android.widget.EditText", label: "Email", value: "oskar@example.com", editable: true, password: false),
            N("e3", index: 2, parentIndex: 0, depth: 1, type: "android.widget.EditText", label: "Password", value: "hunter2", editable: true, password: true, identifier: "com.example:id/password", rect: R(0, 270, 390, 44)),
            N("e4", index: 3, parentIndex: 0, depth: 1, type: "com.example.PinView", label: "PIN", value: "1234", editable: true, password: true),
            N("e5", index: 4, parentIndex: 0, depth: 1, type: "android.widget.TextView", label: "Forgot?", editable: false));
        var (email, password, pin, note) = (projected.Index[1], projected.Index[2], projected.Index[3], projected.Index[4]);
        Assert.Equal("textbox", email.Node.Role);
        Assert.Equal("Email", email.Node.Name);
        Assert.Equal("oskar@example.com", email.Node.Value);
        Assert.Empty(email.States);
        Assert.Null(email.Node.InputPurpose);
        Assert.Equal("textbox", password.Node.Role);
        Assert.Equal("Password", password.Node.Name);
        Assert.Equal("password", password.Node.InputPurpose);
        Assert.Equal(S(("secure", true)), password.States);
        Assert.Equal("com.example:id/password", password.Node.TestId);
        Assert.Null(password.Node.Value);

        // A custom input class the maps do not know is a textbox once the platform says it is editable.
        Assert.Equal("pin-view", pin.Kind);
        Assert.Equal("textbox", pin.Node.Role);
        Assert.Equal("password", pin.Node.InputPurpose);
        Assert.Equal(S(("secure", true)), pin.States);
        Assert.Null(pin.Node.Value);
        Assert.Equal("text", note.Node.Role);
    }

    [Fact]
    public void Keeps_a_role_that_already_takes_text_when_the_platform_reports_it_editable_and_trusts_the_flag_on_a_control_role()
    {
        var projected = Project(
            N("e1", type: "searchbox", label: "Search", editable: true),
            N("e2", type: "android.widget.Spinner", label: "Country", editable: true),
            N("e3", type: "android.widget.Button", label: "Submit", editable: true),
            N("e4", type: "android.widget.Button", label: "Cancel", editable: false),
            N("e5", type: "android.widget.Button", label: "Later"));
        Assert.Equal(["searchbox", "combobox", "textbox", "button", "button"], Roles(projected));
    }

    [Fact]
    public void Reads_the_screen_title_from_the_navigation_bar_its_inner_text_its_identifier_or_nothing()
    {
        Assert.Equal("General", SnapshotProjection.ScreenTitle(Project(SettingsNodes)));
        Assert.Equal("About", SnapshotProjection.ScreenTitle(Project(N("@e1", type: "navigation-bar", identifier: "About"))));
        var innerText = Project(
            N("@e1", depth: 0, type: "navigation-bar"),
            N("@e2", depth: 1, type: "button", label: "Back"),
            N("@e3", depth: 1, type: "static-text", label: "About"));
        Assert.Equal("About", SnapshotProjection.ScreenTitle(innerText));
        Assert.Null(SnapshotProjection.ScreenTitle(Project(N("@e1", type: "button", label: "Go"))));
    }
}
