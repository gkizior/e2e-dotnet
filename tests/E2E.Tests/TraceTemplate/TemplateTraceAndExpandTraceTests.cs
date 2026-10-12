// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using E2E.Engine;
using static E2E.Tests.StepCache.StepTraceSessionTests;

namespace E2E.Tests.TraceTemplate;

/// <summary>
/// The port templates the typed value, the navigated URL, the scroll text, and the start and end routes of an entry, with a
/// slot for each unique value and a <c>|form</c> spelling. It has no JSON pointers, upload paths, action summaries, summary
/// verdict, descriptor or anchor templating (an anchor naming a unique value is left out), <c>uri</c> or <c>slug</c> spellings, or
/// encoding suffixes to refuse. So these upstream its are not ported: "templates upload paths", "keeps the verdict summary as
/// recorded", "refuses to template a recording whose text already spells a placeholder", "spells a value as the slug an app
/// derives for a record path", and "refuses a placeholder whose encoding it does not know".
/// </summary>
public sealed class TemplateTraceAndExpandTraceTests
{
    private static Dictionary<string, object?> Name(string value) => new() { ["name"] = Values.Unique(value) };

    [Fact]
    public void Leaves_a_trace_without_templates_untouched_and_expands_it_as_a_no_op()
    {
        const string text = "tap option \"Acme\" in row \"Acme\"";

        Assert.Equal(text, CacheKeys.Template(text, null));
        Assert.Equal(text, CacheKeys.Template(text, new Dictionary<string, object?>()));
        Assert.Equal(text, CacheKeys.Template(text, new Dictionary<string, object?> { ["name"] = "Acme" }));
        Assert.Equal(text, CacheKeys.Detemplate(text, null));
        Assert.Equal(text, CacheKeys.Detemplate(text, new Dictionary<string, object?>()));

        // A marked value the text never spelled leaves no slot; the recording is literal.
        Assert.Equal(text, CacheKeys.Template(text, Name("Globex")));
    }

    /// <summary>
    /// Partial port of "refuses to template when two marked params share a spelling": the port reports two marked params
    /// with one spelling, and does not look at a value that is another's encoded form.
    /// </summary>
    [Fact]
    public void Reports_two_marked_params_that_share_a_spelling_as_a_collision()
    {
        var shared = new Dictionary<string, object?> { ["name"] = Values.Unique("Acme Corp"), ["slug"] = Values.Unique("Acme Corp") };

        Assert.True(CacheKeys.Collides(shared));
        Assert.False(CacheKeys.Collides(Name("Acme Corp")));
    }

    /// <summary>
    /// Partial port of "reports a unique() value that another param spells, in any of its spellings, as a collision": the
    /// port compares the top-level values as text, so a value nested in another param, a number, a flag, or an encoded form is not reported.
    /// </summary>
    [Fact]
    public void Reports_a_unique_value_that_another_top_level_param_spells_exactly_as_a_collision()
    {
        // The repro: a run-unique title that happened to equal the plain choice.
        Assert.True(CacheKeys.Collides(new Dictionary<string, object?> { ["title"] = Values.Unique("Daily"), ["frequency"] = "Daily" }));

        // Distinct values do not, nor does a plain value spelled inside the marked one.
        Assert.False(CacheKeys.Collides(new Dictionary<string, object?> { ["title"] = Values.Unique("Cabin xyz"), ["frequency"] = "Daily" }));
        Assert.False(CacheKeys.Collides(new Dictionary<string, object?> { ["label"] = Values.Unique("Pine Way"), ["street"] = "Way" }));
        Assert.False(CacheKeys.Collides(new Dictionary<string, object?> { ["title"] = Values.Unique("Daily") }));
        Assert.False(CacheKeys.Collides(null));
    }

    /// <summary>
    /// Partial port of "spells the form encoding as a form submission does, and skips the encodings of a value that has none":
    /// the port has no <c>uri</c> spelling, so only the form spelling and the unpaired surrogate are checked.
    /// </summary>
    [Fact]
    public void Spells_the_form_encoding_as_a_form_submission_does_and_skips_the_encoding_of_a_value_that_has_none()
    {
        Assert.Equal(
            "?q=\u0001q|form\u0001",
            CacheKeys.Template("?q=Ada%27s+%28new%29+shop%7E%21", new Dictionary<string, object?> { ["q"] = Values.Unique("Ada's (new) shop~!") }));

        // An unpaired surrogate cannot be percent-encoded; the value itself still templates and staging does not throw.
        var odd = "bad \ud800 value";
        Assert.Equal("typed \u0001v\u0001", CacheKeys.Template("typed " + odd, new Dictionary<string, object?> { ["v"] = Values.Unique(odd) }));
    }

    /// <summary>
    /// Partial port of "templates the location paths too, in every spelling a URL gives the value, so a value in the URL follows
    /// the run": the port spells a value as typed and as a form, not as a URI path segment.
    /// </summary>
    [Fact]
    public void Templates_the_location_in_the_typed_and_form_spellings_so_a_value_in_the_URL_follows_the_run()
    {
        var recorded = CacheKeys.Template("/companies?search=Acme+%26+Co&name=Acme & Co", Name("Acme & Co"));

        Assert.Equal("/companies?search=\u0001name|form\u0001&name=\u0001name\u0001", recorded);
        Assert.Equal("/companies?search=Globex+Inc&name=Globex Inc", CacheKeys.Detemplate(recorded, Name("Globex Inc")));

        // A value no encoding changes has one spelling and one placeholder.
        Assert.Equal("/tags/\u0001tag\u0001?q=\u0001tag\u0001", CacheKeys.Template("/tags/vip?q=vip", new Dictionary<string, object?> { ["tag"] = Values.Unique("vip") }));
    }

    /// <summary>
    /// Partial port of "templates inputs, descriptors, anchors, and action summaries, and expands them with another run's
    /// value": the typed value and the navigated URL carry the slot and fill from the next run, while descriptors and anchors
    /// that name the value are left out of the entry, and there are no summaries.
    /// </summary>
    [Fact]
    public async Task Templates_the_typed_value_and_the_URL_and_expands_them_with_another_runs_value()
    {
        var typed = new List<string>();
        static string Url(string name) => "/companies/create?name=" + Uri.EscapeDataString(name).Replace("%20", "+", StringComparison.Ordinal);
        var world = new DocumentWorld()
            .Map("/home", page => page.Heading("Home"))
            .Map("/companies/create", page =>
            {
                page.Status("Company form");
                page.Roots.Add(new DocumentElement { Role = "textbox", Name = "Name", Value = "", OnFill = typed.Add });
            });
        static Task Form(TestContext ctx) => Expect.That(ctx.Screen.GetByRole("status", "Company form")).ToBeVisibleAsync();
        var directory = CoreTests.TempCache();
        var first = await StepAsync(
            world,
            new FileStepCache(directory),
            "/home",
            Form,
            Script(ModelResponses.Call("navigate", new { url = Url("E2E abc Company") }), ModelResponses.Fill("textbox", "Name", "E2E abc Company")),
            Name("E2E abc Company"));
        Assert.Null(first.Error);

        var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.json"))), JsonDefaults())!;
        Assert.Equal("/companies/create?name=\u0001name|form\u0001", entry.Actions[0].Url);
        Assert.Equal("\u0001name\u0001", entry.Actions[1].Value);
        Assert.DoesNotContain("E2E", File.ReadAllText(Assert.Single(Directory.GetFiles(directory, "*.json"))), StringComparison.Ordinal);

        typed.Clear();
        var replay = await StepAsync(world, new FileStepCache(directory), "/home", Form, new ScriptedModel(_ => throw new InvalidOperationException("no model call expected")), Name("E2E xyz Company"));

        Assert.Null(replay.Error);
        Assert.Equal(1, replay.Replayed);
        Assert.Equal(["E2E xyz Company"], typed);
    }

    private static JsonSerializerOptions JsonDefaults() => new(JsonSerializerDefaults.Web);
}
