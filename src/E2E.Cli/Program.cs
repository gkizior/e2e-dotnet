// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using System.Globalization;
using System.Text;
using E2E.Cli.Mcp;
using E2E.OAuth;

namespace E2E.Cli;

/// <summary>
/// <c>e2e login</c>, <c>e2e logout</c>, and <c>e2e models</c>: upstream's subscription commands. Logins go to the
/// credentials file upstream's CLI uses, so a login made with either one serves both. <c>e2e guide</c> prints
/// the bundled skill, and <c>e2e mcp</c> serves a coding agent over MCP.
/// </summary>
public static class Program
{
    private const string Usage = """
        Usage:
          e2e login [provider] [--device] [--client-id <id>] [--from-gh] [--enterprise-url <host>]
          e2e logout [provider]
          e2e models [provider]
          e2e guide [topic]
          e2e mcp [--config <path>] [--target <name>] [--headed] [--max-sessions <n>]

        Providers:
          openai            ChatGPT Plus/Pro, the Codex sign-in (--device for a machine without a browser)
          github-copilot    GitHub Copilot (gh signed in, or --client-id of your OAuth App; --enterprise-url for GHE)
          opencode-console  OpenCode Console workspace (OpenCode Zen and Go)
          spacexai          SuperGrok or X Premium+

        e2e mcp serves a coding agent such as Claude Code over MCP (stdio): the fixed tools and the guide
        resources. Register it with: claude mcp add e2e -- e2e mcp

        Credentials live in $XDG_CONFIG_HOME/e2e/oauth.json (or ~/.config/e2e/oauth.json).
        E2E_OAUTH_CREDENTIALS holding the same JSON stands in for the file. Use API keys in CI.
        """;

    public static async Task<int> Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };

        try
        {
            return await RunAsync(args, Console.Out, Console.Error, Console.In, !Console.IsInputRedirected && !Console.IsOutputRedirected, cancel.Token).ConfigureAwait(false);
        }
        catch (E2EException ex)
        {
            await Console.Error.WriteLineAsync("error " + ex.Code + ": " + Clean(ex.Message)).ConfigureAwait(false);
            return 1;
        }
    }

    /// <summary>Runs one command. The writers and reader are test seams.</summary>
    public static async Task<int> RunAsync(string[] args, TextWriter stdout, TextWriter stderr, TextReader stdin, bool interactive, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            await stdout.WriteLineAsync(Usage).ConfigureAwait(false);
            return args.Length == 0 ? 1 : 0;
        }

        if (args[0] == "mcp")
        {
            return await McpAsync(args.Skip(1), stdout, stderr, cancellationToken).ConfigureAwait(false);
        }

        var (positional, flags) = Parse(args.Skip(1));
        var provider = positional.FirstOrDefault();
        switch (args[0])
        {
            case "login":
                return await LoginAsync(provider, flags, stdout, stdin, interactive, cancellationToken).ConfigureAwait(false);
            case "logout":
                return await LogoutAsync(provider, stdout, stdin, interactive, cancellationToken).ConfigureAwait(false);
            case "models":
                return await ModelsAsync(provider, stdout, stderr, cancellationToken).ConfigureAwait(false);
            case "guide":
                return await GuideAsync(positional.FirstOrDefault(), stdout, stderr).ConfigureAwait(false);
            default:
                await stderr.WriteLineAsync("unknown command \"" + args[0] + "\"\n\n" + Usage).ConfigureAwait(false);
                return 1;
        }
    }

    private static async Task<int> LoginAsync(string? providerId, Dictionary<string, string?> flags, TextWriter stdout, TextReader stdin, bool interactive, CancellationToken cancellationToken)
    {
        var store = CredentialStores.Default();
        var id = providerId ?? await PickAsync("Which subscription do you want to sign in to?", OAuthProviders.Ids, store, stdout, stdin, interactive, cancellationToken).ConfigureAwait(false);
        var provider = OAuthProviders.Get(id);
        var options = new OAuthLoginOptions
        {
            Device = flags.ContainsKey("device"),
            ClientId = flags.GetValueOrDefault("client-id"),
            FromGitHubCli = flags.ContainsKey("from-gh"),
            EnterpriseUrl = flags.GetValueOrDefault("enterprise-url"),
        };
        var callbacks = new OAuthLoginCallbacks
        {
            OnAuth = info =>
            {
                stdout.WriteLine(info.Instructions);
                stdout.WriteLine(info.Url);
                if (info.UserCode is not null)
                {
                    stdout.WriteLine("Code: " + info.UserCode);
                }

                if (interactive)
                {
                    OpenBrowser(info.Url);
                }
            },
            OnPrompt = async (message, token) =>
            {
                if (!interactive)
                {
                    throw new OAuthException(OAuthException.Misconfigured, "the browser did not return and there is no terminal to paste the code into");
                }

                await stdout.WriteAsync(message + ": ").ConfigureAwait(false);
                return await stdin.ReadLineAsync(token).ConfigureAwait(false) ?? "";
            },
            OnProgress = message => stdout.WriteLine(message),
        };

        var credentials = await OAuthProviders.LoginAsync(id, callbacks, options, store, cancellationToken).ConfigureAwait(false);
        await stdout.WriteLineAsync(provider.Name + " login stored" + (credentials.Expires == 0 ? "." : "; the token refreshes itself.")).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> LogoutAsync(string? providerId, TextWriter stdout, TextReader stdin, bool interactive, CancellationToken cancellationToken)
    {
        var store = CredentialStores.Default();
        string id;
        if (providerId is null)
        {
            var stored = (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Where(OAuthProviders.Ids.Contains).ToList();
            if (stored.Count == 0)
            {
                await stdout.WriteLineAsync("No logins stored.").ConfigureAwait(false);
                return 0;
            }

            id = await PickAsync("Which login do you want to forget?", stored, store, stdout, stdin, interactive, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            id = providerId;
        }

        var name = OAuthProviders.Get(id).Name;
        var had = await OAuthProviders.LogoutAsync(id, store, cancellationToken).ConfigureAwait(false);
        await stdout.WriteLineAsync(had ? "Signed out of " + name + "." : "No " + name + " login was stored.").ConfigureAwait(false);
        return 0;
    }

    /// <summary>One line per model: the id a config passes, the vendor's name for it, and what the vendor says about it.</summary>
    private static async Task<int> ModelsAsync(string? providerId, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        var store = CredentialStores.Default();
        IReadOnlyList<string> ids;
        if (providerId is null)
        {
            ids = (await store.ListAsync(cancellationToken).ConfigureAwait(false)).Where(OAuthProviders.Ids.Contains).ToList();
            if (ids.Count == 0)
            {
                throw new OAuthException(OAuthException.NotLoggedIn, "no login is stored; sign in with e2e login <" + string.Join('|', OAuthProviders.Ids) + ">");
            }
        }
        else
        {
            ids = [providerId];
        }

        var failed = false;
        foreach (var (id, index) in ids.Select((id, index) => (id, index)))
        {
            if (index > 0)
            {
                await stdout.WriteLineAsync().ConfigureAwait(false);
            }

            var name = OAuthProviders.Get(id).Name;
            IReadOnlyList<SubscriptionModel> models;
            try
            {
                models = await OAuthProviders.ListModelsAsync(id, store, cancellationToken).ConfigureAwait(false);
            }
            catch (E2EException ex) when (ids.Count > 1)
            {
                failed = true;
                await stderr.WriteLineAsync(name + " (" + id + "): " + Clean(ex.Message)).ConfigureAwait(false);
                continue;
            }

            var count = models.Count == 0 ? "no models listed" : models.Count.ToString(CultureInfo.InvariantCulture) + (models.Count == 1 ? " model" : " models");
            await stdout.WriteLineAsync(name + " (" + id + "): " + count).ConfigureAwait(false);
            var width = models.Count == 0 ? 0 : models.Max(model => Clean(model.Id).Length);
            foreach (var model in models)
            {
                var rest = new[] { model.Name, model.Detail }.Where(part => !string.IsNullOrEmpty(part) && part != model.Id).Select(part => Clean(part!)).ToList();
                await stdout.WriteLineAsync("  " + (rest.Count == 0 ? Clean(model.Id) : Clean(model.Id).PadRight(width) + "  " + string.Join("  ", rest))).ConfigureAwait(false);
            }
        }

        return failed ? 1 : 0;
    }

    /// <summary><c>e2e guide</c>: prints the bundled skill's overview, or one topic. An unknown topic exits 2.</summary>
    private static async Task<int> GuideAsync(string? topic, TextWriter stdout, TextWriter stderr)
    {
        var text = Skill.ReadGuide(topic);
        if (text is null)
        {
            await stderr.WriteLineAsync("unknown topic \"" + topic + "\"; topics: " + string.Join(", ", Skill.Topics())).ConfigureAwait(false);
            return 2;
        }

        await stdout.WriteAsync(text.EndsWith('\n') ? text : text + "\n").ConfigureAwait(false);
        return 0;
    }

    /// <summary>Parses the flags of <c>e2e mcp</c> and serves. A bad command line exits 2.</summary>
    private static async Task<int> McpAsync(IEnumerable<string> args, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        string? config = null;
        string? target = null;
        var headed = false;
        var maxSessions = SessionHost.SessionBounds.Default;
        var positional = 0;
        using var e = args.GetEnumerator();
        while (e.MoveNext())
        {
            var arg = e.Current;
            var eq = arg.IndexOf('=', StringComparison.Ordinal);
            var name = eq >= 0 ? arg[..eq] : arg;
            void NoValue()
            {
                if (eq >= 0)
                {
                    throw new ArgumentException("option '" + name + "' takes no argument");
                }
            }

            string? Value(string label) => eq >= 0 ? arg[(eq + 1)..] : e.MoveNext() ? e.Current : throw new ArgumentException("option '" + name + " " + label + "' argument missing");
            try
            {
                switch (name)
                {
                    case "--config":
                        config = Value("<path>");
                        break;
                    case "--target":
                        target = Value("<name>");
                        break;
                    case "-h" or "--help":
                        await stdout.WriteLineAsync(Usage).ConfigureAwait(false);
                        return 0;
                    case "--headed":
                        NoValue();
                        headed = true;
                        break;
                    case "--headless":
                        NoValue();
                        // Sessions were headed by default before upstream 0.18 and took --headless; a client config
                        // that still passes it asks for the default now, and a refused flag would surface as
                        // nothing but a failed connection.
                        break;
                    case "--max-sessions":
                        var value = Value("<n>");
                        var (min, max, _) = SessionHost.SessionBounds;
                        maxSessions = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= min && parsed <= max
                            ? parsed
                            : throw new ArgumentException("option '--max-sessions <n>' argument '" + value + "' is invalid. must be an integer from " + min.ToString(CultureInfo.InvariantCulture) + " through " + max.ToString(CultureInfo.InvariantCulture));
                        break;
                    case var _ when arg.StartsWith('-'):
                        throw new ArgumentException("unknown option '" + arg + "'");
                    default:
                        positional++;
                        break;
                }
            }
            catch (ArgumentException ex)
            {
                await stderr.WriteLineAsync("error: " + ex.Message).ConfigureAwait(false);
                return 2;
            }
        }

        if (positional > 0)
        {
            await stderr.WriteLineAsync("error: too many arguments for 'mcp'. Expected 0 arguments but got " + positional.ToString(CultureInfo.InvariantCulture) + ".").ConfigureAwait(false);
            return 2;
        }

        var options = new McpCommandOptions { Config = config, Target = target, Headed = headed, MaxSessions = maxSessions };
        return await McpCommand.RunAsync(options, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> PickAsync(string question, IReadOnlyList<string> ids, ICredentialStore store, TextWriter stdout, TextReader stdin, bool interactive, CancellationToken cancellationToken)
    {
        if (!interactive)
        {
            throw new OAuthException(OAuthException.Misconfigured, "name a provider: " + string.Join(", ", ids));
        }

        var stored = await store.ListAsync(cancellationToken).ConfigureAwait(false);
        await stdout.WriteLineAsync(question).ConfigureAwait(false);
        for (var i = 0; i < ids.Count; i++)
        {
            var mark = stored.Contains(ids[i]) ? " (signed in)" : "";
            await stdout.WriteLineAsync("  " + (i + 1).ToString(CultureInfo.InvariantCulture) + ") " + ids[i] + mark + ": " + OAuthProviders.Describe(ids[i])).ConfigureAwait(false);
        }

        await stdout.WriteAsync("> ").ConfigureAwait(false);
        var answer = (await stdin.ReadLineAsync(cancellationToken).ConfigureAwait(false) ?? "").Trim();
        if (int.TryParse(answer, NumberStyles.Integer, CultureInfo.InvariantCulture, out var choice) && choice >= 1 && choice <= ids.Count)
        {
            return ids[choice - 1];
        }

        return ids.Contains(answer) ? answer : throw new OAuthException(OAuthException.Cancelled, "the login was cancelled");
    }

    private static (List<string> Positional, Dictionary<string, string?> Flags) Parse(IEnumerable<string> args)
    {
        var positional = new List<string>();
        var flags = new Dictionary<string, string?>(StringComparer.Ordinal);
        using var e = args.GetEnumerator();
        while (e.MoveNext())
        {
            var arg = e.Current;
            if (!arg.StartsWith("--", StringComparison.Ordinal))
            {
                positional.Add(arg);
                continue;
            }

            var name = arg[2..];
            var eq = name.IndexOf('=', StringComparison.Ordinal);
            if (eq >= 0)
            {
                flags[name[..eq]] = name[(eq + 1)..];
            }
            else if (name is "client-id" or "enterprise-url")
            {
                flags[name] = e.MoveNext() ? e.Current : throw new OAuthException(OAuthException.Misconfigured, "--" + name + " needs a value");
            }
            else
            {
                flags[name] = null;
            }
        }

        return (positional, flags);
    }

    /// <summary>Vendor text is untrusted: terminal control characters are dropped.</summary>
    private static string Clean(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!char.IsControl(c) || c is '\n' or '\t')
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static void OpenBrowser(string url)
    {
        try
        {
            var start = OperatingSystem.IsWindows()
                ? new ProcessStartInfo(url) { UseShellExecute = true }
                : new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open", url) { UseShellExecute = false, RedirectStandardError = true, RedirectStandardOutput = true };
            using var _ = Process.Start(start);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // No opener: the URL is printed above.
        }
    }
}
