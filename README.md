# E2E for .NET

Community [.NET](https://dotnet.microsoft.com) port of [e2e](https://github.com/tester-army/e2e), the agentic end-to-end testing framework. Describe a goal in natural language, let an agent drive the app, then check the result with locators.

This is **not** an official TesterArmy product and is not endorsed by TesterArmy. It reimplements the public testing flow. It does not copy the TypeScript sources. Behavior that is intentionally different is listed in [COMPATIBILITY.md](COMPATIBILITY.md).

License: Apache License 2.0. Copyright 2026 TesterArmy.

## Install

```bash
dotnet add package E2E
dotnet add package E2E.NUnit
```

The library targets `net10.0` and includes `WebEngine`, which drives Chromium through [Microsoft.Playwright](https://playwright.dev/dotnet/). Firefox and WebKit are not exposed yet. Install a browser once, from the build output of the project that references `E2E`:

```bash
pwsh bin/Debug/net10.0/playwright.ps1 install chromium
```

## A test

```csharp
using E2E;
using E2E.NUnit;
using NUnit.Framework;

public sealed class BillingTests : E2ETest
{
    protected override IAgentModel? CreateModel() =>
        new OpenAiCompatibleModel(new OpenAiCompatibleModelOptions { Model = "gpt-4.1-mini" });

    [Test]
    public async Task Member_upgrades_to_Pro()
    {
        await App.OpenAsync("/settings/billing");
        await Agent.ActAsync("upgrade the workspace to the Pro plan");
        await Agent.AssertAsync("the invoice preview shows a prorated amount");
        await Expect.That(Screen.GetByRole("status")).ToContainTextAsync("Pro");
    }
}
```

`E2ETest` starts a `WebEngine` session for each `[Test]`. Override `CreateEngine` with a `DocumentEngine` when the test should not open a browser. An `act` that a later `assert`, `waitFor`, or locator `Expect` verifies is recorded. The next run replays those actions with no model calls until the screen no longer matches. When the test ends, verified acts are written and unverified acts that recorded or replayed are evicted, whether it passed, failed, or was skipped. `[Retry]` runs the later attempts live, and they still record. Tests that never call the agent need no model.

## Config

`E2ETest` reads the nearest `e2e.config.json` above the test assembly directory, then above the working directory. The shape follows upstream `e2e.config.ts`. Every key is optional, and an unknown key fails with `INVALID_CONFIG`.

```json
{
  "targets": [{ "platform": "web", "app": { "url": "http://127.0.0.1:4173" } }],
  "timeout": 120000,
  "launchTimeout": 60000,
  "actionTimeout": 30000,
  "assertionTimeout": 5000,
  "cleanupTimeout": 30000,
  "agents": {
    "default": {
      "model": "gpt-4.1-mini",
      "baseUrl": "https://api.openai.com/v1",
      "apiKeyEnv": "OPENAI_API_KEY"
    }
  },
  "cache": { "mode": "read-write", "dir": ".e2e/cache", "strict": false },
  "secrets": { "stripe-key": null }
}
```

There is no default model and no shared API key. `baseUrl` can point at any OpenAI-compatible server, including a local one. Tests without agent steps ignore it.

`provider` picks who serves `model` and `judge`. Unset, it is `openai` (chat completions). `baseUrl` and `apiKeyEnv` default to the provider's own:

| `provider` | Key | Notes |
| --- | --- | --- |
| `openai` | `OPENAI_API_KEY` | Chat completions |
| `openai-responses` | `OPENAI_API_KEY` | The Responses API, as the AI SDK's `openai(id)` |
| `azure` | `AZURE_API_KEY` | Responses API at `https://$AZURE_RESOURCE_NAME.openai.azure.com/openai/v1`, or `baseUrl`. `model` is the deployment |
| `anthropic` | `ANTHROPIC_API_KEY` | Messages API, with prompt-cache breakpoints |
| `google` | `GOOGLE_GENERATIVE_AI_API_KEY` | Gemini API |
| `bedrock` | AWS credentials, or `AWS_BEARER_TOKEN_BEDROCK` | Converse API in `AWS_REGION` |
| `xai` | `XAI_API_KEY` | SpaceXAI API |
| `openrouter` | `OPENROUTER_API_KEY` | Model ids such as `openai/gpt-6-luna-fast` |
| `gateway` | `AI_GATEWAY_API_KEY`, else `VERCEL_OIDC_TOKEN` | Vercel AI Gateway |
| `openai-compatible` | `LLM_API_KEY`, optional | Any `/v1/chat/completions` server; `baseUrl` is required |
| `chatgpt`, `copilot`, `grok`, `opencode-console` | The stored login | Subscriptions, below |

In code, `ModelProviders` builds the same clients (`ModelProviders.Anthropic("claude-sonnet-5")`), and `AnthropicModel`, `GoogleModel`, `BedrockModel`, `OpenAiResponsesModel`, and `OpenAiCompatibleModel` take full options.

### Subscriptions

A ChatGPT Plus/Pro, GitHub Copilot, OpenCode Console (Zen and Go), or SuperGrok/X Premium+ plan can serve the agent instead of an API key. Sign in once with the `e2e` tool:

```bash
dotnet tool install --global E2E.Cli
e2e login openai            # or github-copilot, opencode-console, spacexai
e2e models openai           # the model ids the plan serves
```

Then set `"provider": "chatgpt"` (or `copilot`, `opencode-console`, `grok`) with one of those ids, or use `Subscriptions.ChatGpt("gpt-6-luna")` in code. `e2e login openai --device` works without a browser, and `e2e login github-copilot` reuses `gh auth token` (or takes `--client-id` of your own OAuth App, and `--enterprise-url` for GitHub Enterprise). The login is stored in `~/.config/e2e/oauth.json`, the file upstream's `npx e2e login` writes, so a login made with either tool serves both. `E2E_OAUTH_CREDENTIALS` holding that JSON stands in for the file, and `OPENCODE_API_KEY` replaces an OpenCode Console login. Use API keys in CI.

An agent entry also takes `judge` (the model id for `assert`, `waitFor`, and `extract`), `system`, `context`, `maxSteps`, `maxModelCalls`, `judgmentTimeout`, and `providerOptions`. `agents` can name more agents than `default`; a call picks one with its `Agent` option, such as `new ActOptions { Agent = "careful" }`.

`cache.mode` is `off`, `read-only`, or `read-write`. Unset, it is `read-write` locally and `read-only` when `CI` is set. `cache.strict` fails a recording that no longer matches with `REPLAY_STALE` instead of running the step live. `cache.dir` resolves against the config file's directory.

Each secret reads `E2E_SECRET_<NAME>` first, then the config value. `null` means the variable is required. A test gets one with `Secrets.Get("stripe-key")`.

A fixture overrides any value with the matching property, such as `BaseUrl`, `CacheMode`, or `ActionTimeout`, or replaces the whole config by overriding `Config`.

## Sample

The sample is that billing test as an NUnit project. It uses the document engine and a scripted model, so it needs no browser and no API key. The second test replays the tap and does not ask the model to act.

```bash
dotnet test --project samples/E2E.Sample
```

## Tests

```bash
dotnet test E2E.slnx -c Release
```

Most unit tests use `DocumentEngine` and a scripted model. No test needs an API key. The Playwright tests install Chromium once per run, before the first of them starts, so the first run needs network access.

## What is in this port

| JavaScript | .NET |
| --- | --- |
| `e2e` test, expect, agent, cache, and `@e2e-dev/web` | `E2E` (`WebEngine`) |
| NUnit | `E2E.NUnit` (`E2ETest`) |
| `e2e login`, `e2e logout`, `e2e models` | `E2E.Cli` (the `e2e` .NET tool) |

`@e2e-dev/mobile`, `@e2e-dev/github`, `@e2e-dev/kernel`, and `@e2e-dev/eas` are not ported. Details are in [COMPATIBILITY.md](COMPATIBILITY.md).
