// Copyright 2026 TesterArmy.
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.Loader;
using E2E;
using E2E.Web;

var exit = await RunAsync(args).ConfigureAwait(false);
Environment.Exit(exit);

static async Task<int> RunAsync(string[] args)
{
    if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
    {
        PrintHelp();
        return args.Length == 0 ? 1 : 0;
    }

    try
    {
        return args[0] switch
        {
            "run" => await RunTestsAsync(args.Skip(1).ToArray()).ConfigureAwait(false),
            "cache" when args.Length > 1 && args[1] == "clear" => ClearCache(args.Skip(2).ToArray()),
            _ => Unknown(args[0]),
        };
    }
    catch (E2EException ex)
    {
        Console.Error.WriteLine(ex.Code + ": " + ex.Message);
        return ex.Code == "ENVIRONMENT_UNAVAILABLE" ? 3 : 1;
    }
}

static int Unknown(string command)
{
    Console.Error.WriteLine("Unknown command '" + command + "'.");
    PrintHelp();
    return 1;
}

static void PrintHelp()
{
    Console.WriteLine(
        """
        e2e — agentic end-to-end tests for .NET

        e2e run [project-or-assembly] [--grep text] [--no-cache] [--config path] [--url url]
        e2e cache clear [--config path]

        The project is built, then every public method marked [E2ETest] runs.
        Web targets need Playwright's Chromium. Tests with no agent steps need no model.
        """);
}

static async Task<int> RunTestsAsync(string[] args)
{
    string? project = null;
    string? grep = null;
    string? configPath = null;
    string? url = null;
    var cache = true;
    for (var i = 0; i < args.Length; i++)
    {
        var arg = args[i];
        if (arg == "--no-cache")
        {
            cache = false;
        }
        else if (arg == "--grep" && i + 1 < args.Length)
        {
            grep = args[++i];
        }
        else if (arg == "--config" && i + 1 < args.Length)
        {
            configPath = args[++i];
        }
        else if (arg == "--url" && i + 1 < args.Length)
        {
            url = args[++i];
        }
        else if (!arg.StartsWith('-') && project is null)
        {
            project = arg;
        }
        else
        {
            throw new TestException("INVALID_ARGUMENT", "Unrecognised argument '" + arg + "'.");
        }
    }

    var projectPath = ResolveProject(project);
    var projectDir = Path.GetDirectoryName(projectPath)!;
    Directory.SetCurrentDirectory(projectDir);
    configPath ??= File.Exists(Path.Combine(projectDir, "e2e.config.json"))
        ? Path.Combine(projectDir, "e2e.config.json")
        : null;
    var config = configPath is null ? new E2EConfig() : E2EConfig.Load(configPath);
    url ??= config.App.Url;
    var assemblyPath = Build(projectPath);
    var assembly = Load(assemblyPath);
    var suite = SuiteDiscovery.Discover(assembly);
    IAgentModel? model = null;
    if (config.Agent?.Model is { Length: > 0 } modelName)
    {
        model = new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions
        {
            Model = modelName,
            BaseUrl = string.IsNullOrWhiteSpace(config.Agent.BaseUrl) ? "https://api.openai.com/v1" : config.Agent.BaseUrl,
            ApiKey = config.Agent.ApiKey,
            ApiKeyEnv = config.Agent.ApiKeyEnv,
        });
    }

    var result = await Runner.RunAsync(suite, new RunOptions
    {
        Engine = new WebEngine(),
        Model = model,
        BaseUrl = url,
        CacheDirectory = config.Cache.Directory,
        CacheEnabled = cache && config.Cache.Enabled,
        Grep = grep,
        TestTimeout = TimeSpan.FromMilliseconds(config.Timeouts.TestMs),
        ActionTimeout = TimeSpan.FromMilliseconds(config.Timeouts.ActionMs),
        AssertionTimeout = TimeSpan.FromMilliseconds(config.Timeouts.AssertionMs),
        StepTimeout = TimeSpan.FromMilliseconds(config.Timeouts.StepMs),
    }).ConfigureAwait(false);
    return result.ExitCode;
}

static int ClearCache(string[] args)
{
    string? configPath = null;
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == "--config" && i + 1 < args.Length)
        {
            configPath = args[++i];
        }
    }

    var directory = ".e2e/cache";
    if (configPath is not null)
    {
        directory = E2EConfig.Load(configPath).Cache.Directory;
    }
    else if (File.Exists("e2e.config.json"))
    {
        directory = E2EConfig.Load("e2e.config.json").Cache.Directory;
    }

    if (Directory.Exists(directory))
    {
        Directory.Delete(directory, recursive: true);
    }

    Console.WriteLine("Cleared " + directory);
    return 0;
}

static string ResolveProject(string? project)
{
    if (string.IsNullOrWhiteSpace(project))
    {
        var found = Directory.GetFiles(Directory.GetCurrentDirectory(), "*.csproj");
        if (found.Length != 1)
        {
            throw new TestException("INVALID_ARGUMENT", "Pass a project or assembly. This directory has " + found.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + " projects.");
        }

        return Path.GetFullPath(found[0]);
    }

    var full = Path.GetFullPath(project);
    if (File.Exists(full) && full.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
    {
        return full;
    }

    if (Directory.Exists(full))
    {
        var found = Directory.GetFiles(full, "*.csproj");
        if (found.Length != 1)
        {
            throw new TestException("INVALID_ARGUMENT", "Expected one .csproj in " + full + ".");
        }

        return found[0];
    }

    if (!File.Exists(full))
    {
        throw new TestException("NOT_FOUND", "Could not find " + project + ".");
    }

    return full;
}

static string Build(string path)
{
    if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
    {
        return path;
    }

    var psi = new System.Diagnostics.ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = "build \"" + path + "\" -c Release --nologo",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    using var process = System.Diagnostics.Process.Start(psi) ?? throw new TestException("ENVIRONMENT_UNAVAILABLE", "Could not start dotnet.");
    var stdout = process.StandardOutput.ReadToEnd();
    var stderr = process.StandardError.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0)
    {
        throw new TestException("TEST_SETUP_FAILED", "dotnet build failed.\n" + stdout + stderr);
    }

    var name = Path.GetFileNameWithoutExtension(path);
    var dll = Path.Combine(Path.GetDirectoryName(path)!, "bin", "Release", "net10.0", name + ".dll");
    if (!File.Exists(dll))
    {
        throw new TestException("NOT_FOUND", "Build output was not found at " + dll + ".");
    }

    return dll;
}

static Assembly Load(string assemblyPath)
{
    var directory = Path.GetDirectoryName(assemblyPath)!;
    var context = new AssemblyLoadContext("e2e-tests");
    context.Resolving += (_, name) =>
    {
        var loaded = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(assembly => assembly.GetName().Name == name.Name);
        if (loaded is not null)
        {
            return loaded;
        }

        var candidate = Path.Combine(directory, name.Name + ".dll");
        return File.Exists(candidate) ? context.LoadFromAssemblyPath(candidate) : null;
    };
    return context.LoadFromAssemblyPath(assemblyPath);
}
