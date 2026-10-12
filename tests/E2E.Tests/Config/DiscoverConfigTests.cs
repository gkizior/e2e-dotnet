// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Tests.Config;

public sealed class DiscoverConfigTests
{
    [Fact]
    public void Find_stops_at_the_repository_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "e2e-discover", Guid.NewGuid().ToString("n"));
        var cwd = Path.Combine(root, "repo", "a", "b");
        Directory.CreateDirectory(cwd);
        try
        {
            File.WriteAllText(Path.Combine(root, E2EConfig.FileName), "{}");
            // A worktree or submodule has a .git file, not a directory.
            File.WriteAllText(Path.Combine(root, "repo", ".git"), "");

            Assert.Null(E2EConfig.Find(cwd));

            var inside = Path.Combine(root, "repo", E2EConfig.FileName);
            File.WriteAllText(inside, "{}");
            Assert.Equal(inside, E2EConfig.Find(cwd));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Partial: the port has one config file name, so there is no second spelling.</summary>
    [Fact]
    public void Walks_up_from_cwd_to_the_nearest_config_and_roots_the_project_there()
    {
        var root = Path.Combine(Path.GetTempPath(), "e2e-discover", Guid.NewGuid().ToString("n"));
        var nested = Path.Combine(root, "repo", "apps", "web", "src");
        Directory.CreateDirectory(nested);
        try
        {
            File.WriteAllText(Path.Combine(root, "repo", ".git"), "");
            var outer = Path.Combine(root, "repo", E2EConfig.FileName);
            var inner = Path.Combine(root, "repo", "apps", "web", E2EConfig.FileName);
            File.WriteAllText(outer, "{}");
            File.WriteAllText(inner, "{}");

            Assert.Equal(inner, E2EConfig.Find(nested));
            Assert.Equal(Path.Combine(root, "repo", "apps", "web"), E2EConfig.Load(inner).ProjectRoot);
            Assert.Equal(outer, E2EConfig.Find(Path.Combine(root, "repo", "apps")));
            Assert.Equal(Path.Combine(root, "repo"), E2EConfig.Load(outer).ProjectRoot);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Partial: the port reports a missing explicit path as <c>INVALID_CONFIG</c>, where upstream uses <c>CONFIG_NOT_FOUND</c>.</summary>
    [Fact]
    public void Refuses_an_explicit_path_that_does_not_exist()
    {
        var missing = Path.Combine(Path.GetTempPath(), "e2e-discover", Guid.NewGuid().ToString("n"), "missing.config.json");

        var error = Assert.Throws<ConfigurationException>(() => E2EConfig.Load(missing));

        Assert.Equal("INVALID_CONFIG", error.Code);
        Assert.Contains("Cannot read " + missing, error.Message, StringComparison.Ordinal);
    }
}
