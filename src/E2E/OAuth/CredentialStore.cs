// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Text.Json;
using System.Text.Json.Nodes;

namespace E2E.OAuth;

/// <summary>Where credentials live between runs, keyed by provider id.</summary>
public interface ICredentialStore
{
    Task<OAuthCredentials?> GetAsync(string providerId, CancellationToken cancellationToken = default);

    Task SetAsync(string providerId, OAuthCredentials credentials, CancellationToken cancellationToken = default);

    /// <summary>Forgets the login. True when one was stored.</summary>
    Task<bool> RemoveAsync(string providerId, CancellationToken cancellationToken = default);

    /// <summary>Every provider id with stored credentials.</summary>
    Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default);
}

/// <summary>The stores the subscription models and the CLI use.</summary>
public static class CredentialStores
{
    /// <summary>Credentials JSON in the environment, standing in for the file on a machine the user cannot sign in on.</summary>
    public const string EnvironmentVariable = "E2E_OAUTH_CREDENTIALS";

    /// <summary>
    /// <c>$XDG_CONFIG_HOME/e2e/oauth.json</c>, or <c>~/.config/e2e/oauth.json</c>: the file upstream's
    /// <c>e2e login</c> writes, so a login made with either CLI serves both.
    /// </summary>
    public static string DefaultPath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        var root = string.IsNullOrEmpty(configHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : configHome;
        return Path.Combine(root, "e2e", "oauth.json");
    }

    /// <summary><see cref="EnvironmentVariable"/> when set, else the file at <see cref="DefaultPath"/>.</summary>
    public static ICredentialStore Default()
    {
        var fromEnv = Environment.GetEnvironmentVariable(EnvironmentVariable);
        return string.IsNullOrEmpty(fromEnv) ? new FileCredentialStore(DefaultPath()) : new EnvironmentCredentialStore(fromEnv);
    }

    internal static Dictionary<string, JsonNode?> ParseFile(string text, string source)
    {
        JsonNode? parsed;
        try
        {
            parsed = JsonNode.Parse(text);
        }
        catch (JsonException ex)
        {
            throw new OAuthException(OAuthException.Misconfigured, source + " is not valid JSON", ex);
        }

        if (parsed is not JsonObject entries)
        {
            throw new OAuthException(OAuthException.Misconfigured, source + " must hold an object keyed by provider id");
        }

        return entries.ToDictionary(pair => pair.Key, pair => pair.Value?.DeepClone(), StringComparer.Ordinal);
    }
}

/// <summary>
/// One JSON file keyed by provider id, mode 0600. Writes hold an advisory lock and replace the file atomically,
/// so two runs refreshing at once neither tear the file nor lose each other's entries. Entries this store does
/// not understand are kept as they are.
/// </summary>
public sealed class FileCredentialStore : ICredentialStore
{
    private static readonly TimeSpan LockRetry = TimeSpan.FromMilliseconds(25);
    private static readonly TimeSpan LockStale = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan LockWait = TimeSpan.FromSeconds(5);

    public FileCredentialStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        FilePath = Path.GetFullPath(path);
    }

    public string FilePath { get; }

    public Task<OAuthCredentials?> GetAsync(string providerId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Read().TryGetValue(providerId, out var entry) ? OAuthCredentials.FromJson(entry) : null);
    }

    public Task SetAsync(string providerId, OAuthCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        return UpdateAsync(all => all[providerId] = credentials.ToJson(), cancellationToken);
    }

    public async Task<bool> RemoveAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var removed = false;
        await UpdateAsync(all => removed = all.Remove(providerId), cancellationToken).ConfigureAwait(false);
        return removed;
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> ids = Read().Where(pair => OAuthCredentials.FromJson(pair.Value) is not null).Select(pair => pair.Key).ToList();
        return Task.FromResult(ids);
    }

    private Dictionary<string, JsonNode?> Read()
    {
        string text;
        try
        {
            text = File.ReadAllText(FilePath);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new Dictionary<string, JsonNode?>(StringComparer.Ordinal);
        }

        return CredentialStores.ParseFile(text, FilePath);
    }

    private async Task UpdateAsync(Action<Dictionary<string, JsonNode?>> change, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        var lockPath = FilePath + ".lock";
        await AcquireAsync(lockPath, cancellationToken).ConfigureAwait(false);
        try
        {
            var all = Read();
            change(all);
            var json = new JsonObject();
            foreach (var (id, entry) in all)
            {
                json[id] = entry;
            }

            var temp = FilePath + "." + Environment.ProcessId + "." + DateTime.UtcNow.Ticks + ".tmp";
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }

            try
            {
                await using (var stream = new FileStream(temp, options))
                await using (var writer = new StreamWriter(stream))
                {
                    await writer.WriteAsync(json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n").ConfigureAwait(false);
                }

                File.Move(temp, FilePath, overwrite: true);
            }
            catch
            {
                File.Delete(temp);
                throw;
            }
        }
        finally
        {
            File.Delete(lockPath);
        }
    }

    /// <summary>
    /// An advisory lock file created exclusively. A lock older than the stale bound was left by a dead process and
    /// is claimed by renaming it, which only one waiter can win.
    /// </summary>
    private static async Task AcquireAsync(string lockPath, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + LockWait;
        while (true)
        {
            try
            {
                using (new FileStream(lockPath, FileMode.CreateNew, FileAccess.Write))
                {
                }

                return;
            }
            catch (IOException)
            {
                if (!File.Exists(lockPath))
                {
                    continue;
                }

                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(lockPath) > LockStale)
                {
                    try
                    {
                        var claimed = lockPath + "." + Environment.ProcessId + "." + DateTime.UtcNow.Ticks + ".stale";
                        File.Move(lockPath, claimed);
                        File.Delete(claimed);
                    }
                    catch (IOException)
                    {
                        // Another waiter claimed it first; try the lock again.
                    }

                    continue;
                }

                if (DateTime.UtcNow > deadline)
                {
                    throw new OAuthException(OAuthException.Misconfigured, lockPath + " is held by another process; remove it if that process is gone");
                }

                await Task.Delay(LockRetry, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}

/// <summary>Credentials from <see cref="CredentialStores.EnvironmentVariable"/>: readable everywhere, changed nowhere.</summary>
public sealed class EnvironmentCredentialStore : ICredentialStore
{
    private readonly Dictionary<string, JsonNode?> _entries;

    public EnvironmentCredentialStore(string json)
    {
        _entries = CredentialStores.ParseFile(json, CredentialStores.EnvironmentVariable);
    }

    public Task<OAuthCredentials?> GetAsync(string providerId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_entries.TryGetValue(providerId, out var entry) ? OAuthCredentials.FromJson(entry) : null);
    }

    /// <summary>Throws <see cref="OAuthException.Misconfigured"/>: this store never takes a write. Lets a login fail before it starts.</summary>
    internal void AssertWritable()
    {
        throw ReadOnly();
    }

    public Task SetAsync(string providerId, OAuthCredentials credentials, CancellationToken cancellationToken = default)
    {
        throw ReadOnly();
    }

    public Task<bool> RemoveAsync(string providerId, CancellationToken cancellationToken = default)
    {
        throw ReadOnly();
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<string> ids = _entries.Where(pair => OAuthCredentials.FromJson(pair.Value) is not null).Select(pair => pair.Key).ToList();
        return Task.FromResult(ids);
    }

    private static OAuthException ReadOnly()
    {
        return new OAuthException(
            OAuthException.Misconfigured,
            CredentialStores.EnvironmentVariable + " is set, so logins come from the environment and cannot be changed here; unset it to sign in on this machine");
    }
}
