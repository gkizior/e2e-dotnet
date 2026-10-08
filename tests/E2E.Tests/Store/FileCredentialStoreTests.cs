// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.OAuth;

namespace E2E.Tests.Store;

[Collection(ProcessEnvironment.Name)]
public sealed class FileCredentialStoreTests : IDisposable
{
    private static readonly OAuthCredentials Creds = new()
    {
        Access = "a",
        Refresh = "r",
        Expires = 123,
        Extra = new Dictionary<string, string> { ["accountId"] = "acct" },
    };

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "e2e-oauth-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task Creates_the_file_with_owner_only_permissions_and_round_trips_entries()
    {
        var file = Path.Combine(_directory, "nested", "oauth.json");
        var store = new FileCredentialStore(file);
        Assert.Null(await store.GetAsync("spacexai"));
        await store.SetAsync("spacexai", Creds);
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(file)!));
        }

        var read = await store.GetAsync("spacexai");
        Assert.Equal(("a", "r", 123L, "acct"), (read!.Access, read.Refresh, read.Expires, read.Get("accountId")));
        Assert.Equal(["spacexai"], await store.ListAsync());
        await store.RemoveAsync("spacexai");
        Assert.Empty(await store.ListAsync());
        Assert.Equal("{}\n", await File.ReadAllTextAsync(file));
        Assert.False(File.Exists(file + ".lock"));
    }

    [Fact]
    public async Task Drops_a_damaged_entry_instead_of_losing_every_login()
    {
        var file = Path.Combine(_directory, "nested", "oauth.json");
        var store = new FileCredentialStore(file);
        await store.SetAsync("x", Creds);
        await File.WriteAllTextAsync(file, """{"x":{"access":"a","refresh":"r","expires":123,"accountId":"acct"},"broken":{"access":1},"nan":{"access":"a","refresh":"r","expires":null}}""");
        Assert.Equal(["x"], await store.ListAsync());
        Assert.Null(await store.GetAsync("broken"));
    }

    [Fact]
    public async Task Keeps_other_providers_when_one_is_written_even_from_concurrent_writers()
    {
        var file = Path.Combine(_directory, "nested", "oauth.json");
        var a = new FileCredentialStore(file);
        var b = new FileCredentialStore(file);
        await Task.WhenAll(a.SetAsync("a", Creds), b.SetAsync("b", With(Creds, "b")), a.SetAsync("c", With(Creds, "c")));
        Assert.Equal(["a", "b", "c"], (await a.ListAsync()).Order(StringComparer.Ordinal));
        Assert.Equal("b", (await b.GetAsync("b"))?.Access);
    }

    [Fact]
    public async Task Defers_a_writer_while_another_process_holds_the_lock()
    {
        var file = Path.Combine(_directory, "nested", "oauth.json");
        var store = new FileCredentialStore(file);
        await store.SetAsync("x", Creds);
        await File.WriteAllTextAsync(file + ".lock", "");
        var settled = false;
        var pending = store.SetAsync("y", Creds).ContinueWith(_ => settled = true, TaskScheduler.Default);
        await Task.Delay(150);
        Assert.False(settled);
        File.Delete(file + ".lock");
        await pending;
        Assert.Equal(["x", "y"], (await store.ListAsync()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Tightens_the_permissions_of_a_directory_and_file_that_already_existed()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var file = Path.Combine(_directory, "nested", "oauth.json");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.SetUnixFileMode(Path.GetDirectoryName(file)!, (UnixFileMode)Convert.ToInt32("755", 8));
        await File.WriteAllTextAsync(file, "{}");
        File.SetUnixFileMode(file, (UnixFileMode)Convert.ToInt32("644", 8));
        await new FileCredentialStore(file).SetAsync("x", Creds);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(file)!));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(file));
    }

    [Fact]
    public async Task Takes_over_a_stale_lock_left_by_a_dead_process()
    {
        var file = Path.Combine(_directory, "nested", "oauth.json");
        var store = new FileCredentialStore(file);
        await store.SetAsync("x", Creds);
        await File.WriteAllTextAsync(file + ".lock", "");
        File.SetLastWriteTimeUtc(file + ".lock", DateTime.UtcNow.AddSeconds(-60));
        await store.SetAsync("y", Creds);
        Assert.Equal(["x", "y"], (await store.ListAsync()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Defaults_to_the_XDG_config_directory()
    {
        using (ProcessEnvironment.Set("XDG_CONFIG_HOME", "/tmp/xdg"))
        {
            Assert.Equal(Path.Combine("/tmp/xdg", "e2e", "oauth.json"), CredentialStores.DefaultPath());
        }

        using (ProcessEnvironment.Set("XDG_CONFIG_HOME", null))
        {
            Assert.EndsWith(Path.Combine(".config", "e2e", "oauth.json"), CredentialStores.DefaultPath(), StringComparison.Ordinal);
        }
    }

    private static OAuthCredentials With(OAuthCredentials credentials, string access) =>
        new() { Access = access, Refresh = credentials.Refresh, Expires = credentials.Expires, Extra = credentials.Extra };
}
