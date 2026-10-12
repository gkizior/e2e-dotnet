// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;

namespace E2E.Tests.FixtureRuntime;

/// <summary>
/// The port has no step records, no <c>secrets.get</c> outside a run, and no screen without a site. So the upstream its
/// "fills a secret on a target without a site, such as a device whose URL is only a cache anchor" is not ported. The
/// part of "fills a config.secrets value into a plain textbox" that denies a screenshot after a fill is in
/// <c>ScreenshotAssertions.ToHaveScreenshotTests.Keeps_no_screenshot_once_a_secret_was_filled</c>.
/// </summary>
public sealed class GenericSecretsTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "e2e-config-root");

    private static readonly SemanticNode Key = new() { Ref = "key", Role = "textbox", Name = "API key" };

    /// <summary>A screen on the page at <paramref name="route"/> that records what each fill writes.</summary>
    private static Screen ScreenAt(string route, List<LocatorAction> performed)
    {
        var observation = new Observation { Route = route, Roots = [Key] };
        return new Screen(
            _ => Task.FromResult(observation),
            (_, action, _) =>
            {
                performed.Add(action);
                return Task.CompletedTask;
            },
            () => CancellationToken.None,
            () => { },
            ScreenFixture.Timeout,
            ScreenFixture.Timeout);
    }

    /// <summary>Partial: the port has no step records to redact the value from, and its config secret has no <c>generic-secret</c> purpose.</summary>
    [Fact]
    public async Task Fills_a_config_secrets_value_into_a_plain_textbox_without_the_handle_showing_it()
    {
        const string apiKey = "sk_live_generic_2718";
        var config = E2EConfig.Parse("""{ "secrets": { "stripe-key": "sk_live_generic_2718" } }""", Root, _ => null);
        var performed = new List<LocatorAction>();

        var handle = config.Secrets.Get("stripe-key");
        await ScreenAt("https://app.test/", performed).GetByRole("textbox", "API key").FillAsync(handle);

        Assert.Equal("stripe-key", handle.Name);
        Assert.DoesNotContain(apiKey, handle.ToString(), StringComparison.Ordinal);
        Assert.Equal(new LocatorAction.Fill(apiKey, Sensitive: true), Assert.Single(performed));
    }

    [Fact]
    public async Task A_deterministic_fill_is_not_gated_by_origin_a_third_party_sign_in_page()
    {
        var config = E2EConfig.Parse("""{ "secrets": { "key": "sk_live_1" } }""", Root, _ => null);
        var performed = new List<LocatorAction>();

        await ScreenAt("https://accounts.idp.test/", performed).GetByRole("textbox", "API key").FillAsync(config.Secrets.Get("key"));

        Assert.Equal(new LocatorAction.Fill("sk_live_1", Sensitive: true), Assert.Single(performed));
    }

    /// <summary>Partial: the message names the secret and <c>e2e.config.json</c>, where upstream names the <c>E2E_SECRET_UNKNOWN</c> variable.</summary>
    [Fact]
    public void Fails_a_secret_the_config_does_not_declare_with_SECRET_UNAVAILABLE_naming_the_secret()
    {
        var config = E2EConfig.Parse("""{ "secrets": { "known": "known-value" } }""", Root, _ => null);

        var error = Assert.Throws<TestException>(() => config.Secrets.Get("unknown"));

        Assert.Equal("SECRET_UNAVAILABLE", error.Code);
        Assert.Contains("'unknown'", error.Message, StringComparison.Ordinal);
    }
}
