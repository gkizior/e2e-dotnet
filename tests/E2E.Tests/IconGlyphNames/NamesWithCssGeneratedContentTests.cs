// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.RegularExpressions;
using E2E.Engine;

namespace E2E.Tests.IconGlyphNames;

/// <summary>
/// CSS generated content names a control as the browser names it, and an icon font's private-use glyph never
/// does: the tree and the role locator agree on <c>button "Sign in"</c> for Font Awesome's
/// <c>&lt;i class="fa-sign-in"&gt;</c>, on <c>button "→ Next"</c> for a real character, and on a glyph between
/// two words as a space. A glyph-only link reads as its title, the word a person hovering it sees, though the
/// browser names it by the glyph.
/// </summary>
[Collection(BrowserCollection.Name)]
public sealed class NamesWithCssGeneratedContentTests
{
    private const string Page = """
        <!DOCTYPE html>
        <html><body>
        <style>
          .next::before { content: "\2192"; }
          .glyph::before { content: "\f090"; }
          .badge::after { content: attr(data-count) " new"; }
        </style>
        <button><i class="next"> Next</i></button>
        <button><i class="glyph"> Sign in</i></button>
        <a href="/inbox" class="badge" data-count="3">Inbox</a>
        <a href="/leave" title="Sign out"><i class="glyph"></i></a>
        <a href="/bare"><i class="glyph"></i></a>
        <button>Log<i class="glyph"></i>out</button>
        <label class="next">Email <input></label>
        <label><i class="glyph"></i> Phone <input></label>
        <h2 data-testid="don't">Don't "quote" a &gt;&gt; b</h2>
        </body></html>
        """;

    [Fact]
    public async Task Reads_generated_text_into_a_name_reads_icon_glyphs_as_spaces_and_names_a_glyph_only_link_by_its_title_or_not_at_all()
    {
        using var site = await TinySite.StartAsync(Page);
        await using var engine = await WebSemanticsTests.OpenAsync(site.Url, new WebEngineOptions { Headless = true });
        var observation = await engine.ObserveAsync(CancellationToken.None);
        var read = WebSemanticsTests.Flatten(observation.Roots)
            .Where(node => node.Role is "button" or "link")
            .Select(node => (node.Role, node.Name))
            .ToList();
        Assert.Equal(
            [
                ("button", "→ Next"),
                ("button", "Sign in"),
                ("link", "Inbox3 new"),
                ("link", "Sign out"),
                ("link", null),
                ("button", "Log out"),
            ],
            read);
    }

    [Fact]
    public async Task Finds_each_control_by_the_name_the_tree_reports()
    {
        await WithScreenAsync(async screen =>
        {
            Assert.Equal(["→ Next"], await NamesAsync(screen.GetByRole("button", "→ Next")));
            Assert.Equal(["Sign in"], await NamesAsync(screen.GetByRole("button", "Sign in")));
            Assert.Equal(["Inbox3 new"], await NamesAsync(screen.GetByRole("link", "Inbox3 new")));
            Assert.Equal(["Log out"], await NamesAsync(screen.GetByRole("button", "Log out")));
        });
    }

    [Fact]
    public async Task Matches_a_substring_across_a_glyph_and_stays_exact_where_asked()
    {
        await WithScreenAsync(async screen =>
        {
            Assert.Equal(["Sign in"], await NamesAsync(screen.GetByRole("button", "sign IN", exact: false)));
            Assert.Empty(await NamesAsync(screen.GetByRole("button", "Sign")));
            Assert.Empty(await NamesAsync(screen.GetByRole("button", "Next")));
            Assert.Equal(["Inbox3 new"], await NamesAsync(screen.GetByRole("link", "new", exact: false)));
        });
    }

    [Fact]
    public async Task Matches_a_name_with_quotes_and_however_the_query_is_chained()
    {
        const string quoted = "Don't \"quote\" a >> b";
        await WithScreenAsync(async screen =>
        {
            Assert.Equal([quoted], await NamesAsync(screen.GetByRole("heading", quoted).First()));
            Assert.Equal([quoted], await NamesAsync(screen.GetByRole("heading", "don't \"QUOTE\"", exact: false).First()));
            Assert.Equal([quoted], await NamesAsync(screen.GetByRole("heading", "Don't", new RoleOptions { Exact = false, Visible = true })));
            Assert.Equal([quoted], await NamesAsync(screen.GetByText(new Regex(Regex.Escape(quoted))).First()));
            Assert.Equal([quoted], await NamesAsync(screen.GetByText(new Regex("""[']t "quote" a >>""")).First()));
            Assert.Equal([quoted], await NamesAsync(screen.GetByTestId(new Regex("^don't$")).First()));
        });
    }

    private static async Task WithScreenAsync(Func<Screen, Task> body)
    {
        using var site = await TinySite.StartAsync(Page);
        var session = await WebSemanticsTests.StartSessionAsync(site.Url);
        await WebSemanticsTests.RunAsync(session, async () =>
        {
            await session.App.OpenAsync("/");
            await body(session.Screen);
        });
    }

    private static async Task<List<string?>> NamesAsync(Locator locator) =>
        (await locator.ResolveAsync(CancellationToken.None)).Select(node => node.Name).ToList();
}
