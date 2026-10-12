// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.Config;

public sealed class ResolveConfigTests
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "e2e-config-root");

    [Fact]
    public void Secrets_that_share_an_override_variable_are_refused_without_naming_a_value()
    {
        string[] values = ["secret-value-a1", "secret-value-b2", "secret-value-c3"];
        var colliding = $$"""{ "secrets": { "api-key": "{{values[0]}}", "api_key": "{{values[1]}}" } }""";
        foreach (var environment in new[] { Env(), Env(("E2E_SECRET_API_KEY", "rotated-value-z9")) })
        {
            var error = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse(colliding, Root, environment));
            Assert.Equal("INVALID_CONFIG", error.Code);
            Assert.Equal(
                "secrets \"api-key\" and \"api_key\" share the override variable E2E_SECRET_API_KEY, so one value set there would replace all of them; rename all but one",
                error.Message);
            foreach (var value in values.Append("rotated-value-z9"))
            {
                Assert.DoesNotContain(value, error.Message, StringComparison.Ordinal);
            }
        }

        var punctuation = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse(
            $$"""{ "secrets": { "api.key": "{{values[0]}}", "API-KEY": "{{values[1]}}", "apikey": "{{values[2]}}" } }""", Root, Env()));
        Assert.StartsWith("secrets \"api.key\" and \"API-KEY\" share the override variable E2E_SECRET_API_KEY", punctuation.Message, StringComparison.Ordinal);

        var three = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse(
            $$"""{ "secrets": { "a-b": "{{values[0]}}", "a_b": "{{values[1]}}", "a.b": "{{values[2]}}" } }""", Root, Env()));
        Assert.StartsWith("secrets \"a-b\", \"a_b\" and \"a.b\" share", three.Message, StringComparison.Ordinal);

        // Upstream also tests a provider-function secret; the port has only string secrets.
        var pk = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse(
            $$"""{ "secrets": { "pk-live": "{{values[0]}}", "pk_live": "{{values[1]}}" } }""", Root, Env()));
        Assert.Contains("E2E_SECRET_PK_LIVE", pk.Message, StringComparison.Ordinal);
    }

    /// <summary>Partial: the port runs one web target, so the target name defaults to the platform and there is no second platform to name.</summary>
    [Fact]
    public void Defaults_a_web_target_name_to_its_platform()
    {
        Assert.Equal("web", E2EConfig.Parse("""{ "targets": [{ "platform": "web" }] }""", Root, Env()).Target.Name);
        Assert.Equal("web", E2EConfig.Parse("{}", Root, Env()).Target.Name);
    }

    /// <summary>Partial: the port reports an invalid name with its own message, and has no duplicate names to refuse with one target.</summary>
    [Theory]
    [InlineData("bad name")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    public void Refuses_an_invalid_target_name(string name)
    {
        var error = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse($$"""{ "targets": [{ "name": "{{name}}", "platform": "web" }] }""", Root, Env()));
        Assert.Equal("INVALID_CONFIG", error.Code);
        Assert.Contains("targets[0].name must use ASCII letters, numbers, \"_\", \"-\", and \".\", and cannot be only dots", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Partial: the port accepts these names on its one target.</summary>
    [Theory]
    [InlineData(".hidden")]
    [InlineData("v1.2")]
    public void Accepts_a_target_name_that_starts_with_or_holds_a_dot(string name)
    {
        Assert.Equal(name, E2EConfig.Parse($$"""{ "targets": [{ "name": "{{name}}", "platform": "web" }] }""", Root, Env()).Target.Name);
    }

    /// <summary>Partial: the port has no engine to inherit a platform from, so only the unknown key is reported.</summary>
    [Fact]
    public void Points_at_the_entry_when_a_target_has_an_unknown_key()
    {
        var error = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse("""{ "targets": [{ "platform": "web", "browser": "x" }] }""", Root, Env()));
        Assert.Contains("targets[0]", error.Message, StringComparison.Ordinal);
        Assert.Contains("\"browser\"", error.Message, StringComparison.Ordinal);
    }

    /// <summary>Partial: the port requires <c>targets</c> to be a non-empty array when it is present, and defaults the web target when it is absent.</summary>
    [Fact]
    public void Requires_targets_to_be_a_non_empty_array_and_defaults_the_web_target_without_them()
    {
        var error = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse("""{ "targets": {} }""", Root, Env()));
        Assert.Contains("targets must be a non-empty array", error.Message, StringComparison.Ordinal);
        var config = E2EConfig.Parse("{}", Root, Env());
        Assert.Equal("web", config.Target.Name);
        Assert.Equal("web", config.Target.Platform);
    }

    /// <summary>Partial: the port checks the two app shapes it has, the object and its <c>url</c>.</summary>
    [Fact]
    public void Checks_the_shape_of_the_app_fields_the_port_has()
    {
        var url = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse("""{ "targets": [{ "app": { "url": 5 } }] }""", Root, Env()));
        Assert.Contains("targets[0].app.url must be a non-empty string", url.Message, StringComparison.Ordinal);
        var app = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse("""{ "targets": [{ "app": "https://app.test" }] }""", Root, Env()));
        Assert.Contains("targets[0].app must be an object", app.Message, StringComparison.Ordinal);
    }

    /// <summary>Partial: the port suggests the nearest key and points the keys that moved at their new place. It has no <c>tests</c> or engine hints.</summary>
    [Fact]
    public void Suggests_the_nearest_key_and_points_a_moved_key_at_where_it_lives()
    {
        var near = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse("""{ "target": [] }""", Root, Env()));
        Assert.Contains("unknown config key \"target\"; did you mean \"targets\"?", near.Message, StringComparison.Ordinal);
        var moved = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse("""{ "app": { "url": "http://127.0.0.1:4000" } }""", Root, Env()));
        Assert.Contains("unknown config key \"app\"; the app under test is declared on its target", moved.Message, StringComparison.Ordinal);
    }

    /// <summary>Partial: the port names the option and its rule, with no unit or offending value.</summary>
    [Theory]
    [InlineData("""{ "timeout": "30s" }""", "timeout must be a positive integer")]
    [InlineData("""{ "launchTimeout": 0 }""", "launchTimeout must be a positive integer")]
    public void Names_the_option_that_is_not_a_positive_number_of_milliseconds(string json, string message)
    {
        var error = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse(json, Root, Env()));
        Assert.Contains(message, error.Message, StringComparison.Ordinal);
    }

    /// <summary>Partial: the port has string secrets only, no provider functions or origin narrowing.</summary>
    [Fact]
    public void Resolves_config_secrets_with_the_override_variable_winning()
    {
        var config = E2EConfig.Parse(
            """{ "secrets": { "stripe-key": "sk_test_123", "rotated": "stale" } }""",
            Root,
            Env(("E2E_SECRET_ROTATED", "fresh-from-env")));

        Assert.Equal("sk_test_123", config.Secrets.Get("stripe-key").Value);
        Assert.Equal("fresh-from-env", config.Secrets.Get("rotated").Value);
    }

    /// <summary>Partial: the port has no password handles or credentials to share a name with.</summary>
    [Theory]
    [InlineData("""{ "secrets": { "key": "" } }""", null, "secret \"key\" must be a non-empty string")]
    [InlineData("""{ "secrets": { "key": 42 } }""", null, "secret \"key\" must be a non-empty string, or null to read only E2E_SECRET_KEY")]
    [InlineData("""{ "secrets": { "key": { "value": "v" } } }""", null, "secret \"key\" must be a non-empty string, or null to read only E2E_SECRET_KEY")]
    [InlineData("""{ "secrets": { "key": "valid-value" } }""", "E2E_SECRET_KEY", "secret \"key\" must be a non-empty string")]
    public void Rejects_an_empty_or_non_string_secret_and_an_empty_override(string json, string? emptyOverride, string message)
    {
        var environment = emptyOverride is null ? Env() : Env((emptyOverride, ""));
        var error = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse(json, Root, environment));
        Assert.Equal(message, error.Message);
    }

    /// <summary>Partial: the port has no credentials, so only the E2E_SECRET_* half is checked.</summary>
    [Fact]
    public void Accepts_secret_punctuation_that_maps_to_a_distinct_variable()
    {
        var config = E2EConfig.Parse(
            """{ "secrets": { "svc-a": "secret-value-1", "stripe.key": "secret-value-2", "stripe-key-2": "secret-value-3" } }""",
            Root,
            Env(("E2E_SECRET_STRIPE_KEY", "rotated-stripe"), ("E2E_USER_SVC_A_PASSWORD", "rotated-password")));

        Assert.Equal("rotated-stripe", config.Secrets.Get("stripe.key").Value);
        Assert.Equal("secret-value-3", config.Secrets.Get("stripe-key-2").Value);
        Assert.Equal("secret-value-1", config.Secrets.Get("svc-a").Value);
    }

    /// <summary>Partial: the port has no credentials, so only the secrets are checked.</summary>
    [Fact]
    public void Refuses_a_static_secret_under_6_code_points_from_config_or_the_environment()
    {
        var short5 = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse("""{ "secrets": { "pin": "12345" } }""", Root, Env()));
        Assert.Equal("INVALID_CONFIG", short5.Code);
        Assert.Contains(
            "secret \"pin\" must be at least 6 characters (code points), not 5; a shorter value cannot be redacted without rewriting unrelated text",
            short5.Message,
            StringComparison.Ordinal);
        E2EConfig.Parse("""{ "secrets": { "pin": "123456" } }""", Root, Env());
        var overridden = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse("""{ "secrets": { "pin": "long-enough" } }""", Root, Env(("E2E_SECRET_PIN", "7"))));
        Assert.Contains("secret \"pin\" must be at least 6 characters (code points), not 1", overridden.Message, StringComparison.Ordinal);
    }

    /// <summary>Partial: the port has no credentials, so only the secrets are checked.</summary>
    [Fact]
    public void Counts_a_secret_in_code_points_so_three_emoji_are_three_characters_and_not_six()
    {
        Assert.Equal(6, "🔐🔐🔐".Length);
        var error = Assert.Throws<ConfigurationException>(() => E2EConfig.Parse("""{ "secrets": { "pin": "🔐🔐🔐" } }""", Root, Env()));
        Assert.Contains("secret \"pin\" must be at least 6 characters (code points), not 3", error.Message, StringComparison.Ordinal);
        E2EConfig.Parse("""{ "secrets": { "pin": "🔐🔐🔐🔐🔐🔐" } }""", Root, Env());
    }

    private static Func<string, string?> Env(params (string Name, string Value)[] values)
    {
        var map = values.ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);
        return name => map.GetValueOrDefault(name);
    }
}
