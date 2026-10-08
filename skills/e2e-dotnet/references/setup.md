# Setting up e2e for .NET

## Requirements

- The .NET 10 SDK. The packages target `net10.0`.
- NUnit or xUnit v3. xUnit v2 is not supported.
- Chromium. The first `WebEngine` launch in a test run installs the build it
  launches (the headless shell for a headless run), so the first run needs
  network access. Firefox and WebKit are not exposed yet.
- On a CI image that already has the browser, or with no network, set
  `E2E_SKIP_BROWSER_INSTALL=1` and install it yourself from the build output:
  `pwsh bin/Debug/net10.0/playwright.ps1 install chromium`.

## Scaffold

There is no `e2e init`. Create a test project and add the packages:

```bash
dotnet new nunit -n MyApp.E2E
dotnet add MyApp.E2E package E2E
dotnet add MyApp.E2E package E2E.NUnit
```

For xUnit v3, see topic `writing-tests-xunit`. Topic `writing-tests-nunit`
covers the NUnit base class.

Install the `e2e` tool for subscription logins and the MCP server:

```bash
dotnet tool install --global E2E.Cli
```

In a clone of the e2e-dotnet repo, run it as
`dotnet run --project src/E2E.Cli -- <command>`.

## Subscriptions and API keys

Pick the model in `agents.<name>.provider` and `model`:

| Goal | `provider` | Auth |
| --- | --- | --- |
| Claude on a subscription | `copilot` | `e2e login github-copilot` (reuses `gh auth token`, or your own `--client-id`) |
| OpenAI on a subscription | `chatgpt` | `e2e login openai` (`--device` without a browser) |
| OpenCode Console (Zen and Go) | `opencode-console` | `e2e login opencode-console`, or `OPENCODE_API_KEY` |
| SuperGrok or X Premium+ | `grok` | `e2e login spacexai` |
| Claude on an API key | `anthropic` | `ANTHROPIC_API_KEY`, a workspace key (`sk-ant-api03-...`) |
| OpenAI on an API key | `openai` (the default) | `OPENAI_API_KEY` |
| Other | `openai-responses`, `azure`, `google`, `bedrock`, `xai`, `openrouter`, `gateway`, `openai-compatible` | The provider's key; see the README |

- Get model ids from `e2e models <login>`. Copilot uses a dot
  (`claude-sonnet-5.5`). The Anthropic API uses a dash (`claude-sonnet-5-5`).
- `apiKeyEnv` overrides the provider's variable. Remove it when you change
  `provider`, or the client sends no key and gets a 401.
- A Claude Pro or Max plan cannot serve the agent. Use Copilot or an API key.
- Logins live in `~/.config/e2e/oauth.json`, the file upstream's
  `npx e2e login` writes, so a login made with either tool serves both.
- Subscriptions are for local runs. Use API keys in CI.

## The config

`e2e.config.json` sits in the test project folder. `E2ETest` finds the
nearest one above the test assembly, then above the working directory. An
unknown key fails with `INVALID_CONFIG`.

```json
{
  "targets": [{ "platform": "web", "app": { "url": "http://127.0.0.1:3000" } }],
  "actionTimeout": 15000,
  "agents": {
    "default": {
      "provider": "copilot",
      "model": "claude-sonnet-5.5",
      "context": "Facts about the app the model should know, such as which data it must not change."
    }
  },
  "cache": { "dir": ".e2e/cache" },
  "secrets": { "admin-password": null }
}
```

| Key | Default | Notes |
| --- | --- | --- |
| `targets` | a `web` target with no URL | One entry: `name`, `platform` (`web`), and `app.url`. Without a URL, `App.OpenAsync` takes absolute URLs only. |
| `timeout` | `120000` | Per test, ms. |
| `launchTimeout` | `60000` | Engine start, ms. |
| `actionTimeout` | `30000` | Each locator action and engine operation, ms. |
| `assertionTimeout` | `5000` | `Expect` polling window, ms. |
| `cleanupTimeout` | `30000` | Engine cleanup, ms. |
| `retries` | `0` | Checked, but not used: retries come from NUnit `[Retry]`. |
| `agents` | none | `default` and any other named agent. Options: topic `agent`. |
| `cache` | `read-write` | `{ mode, dir, strict }`. CI demotes the mode to `read-only`. |
| `secrets` | `{}` | Named values the model never sees; `null` requires `E2E_SECRET_<NAME>`. |

There are no `credentials`, `tests`, `workers`, `reporters`, `output`,
`trace`, `video`, or `artifacts` keys; each is `INVALID_CONFIG`. Accounts
come from `E2E_USER_<NAME>_USERNAME` and `E2E_USER_<NAME>_PASSWORD`, read
with `Credentials.User(name)`.

A fixture overrides most values with a virtual property of the same name,
such as `BaseUrl`, `CacheMode`, or `ActionTimeout`; named agents come from
`CreateAgents()`. Override `Config` to replace the whole config.

## The app under test

The target declares the app's `url`. `App.OpenAsync(path)` resolves a path
against it. There is no `app.command`: start the app before `dotnet test`,
and point `url` at it.

`WebEngine` takes `WebEngineOptions`. Override `CreateEngine()` to set them:

```csharp
protected override IEngine CreateEngine() => new WebEngine(new WebEngineOptions
{
    Locale = "de-DE",
    TimezoneId = "Europe/Berlin",
});
```

| Option | Meaning |
| --- | --- |
| `Headless` | Unset: headless, unless `E2E_HEADLESS` is `0` or `false`. |
| `Viewport` | Default 1280 by 720; `null` follows the browser window. |
| `TestIdAttribute` | What `GetByTestId` reads; default `data-testid`. |
| `Headers` | Sent only to the base URL's host; turns the browser HTTP cache off. |
| `BasicAuth` | Credentials for a `401` challenge. |
| `UserAgent` | The `User-Agent` every attempt sends. |
| `Locale` | The language every attempt runs in, a BCP 47 tag: `navigator.language`, `Intl`, and `Accept-Language`. An `accept-language` entry in `Headers` beside it is `INVALID_CONFIG`. |
| `TimezoneId` | The IANA time zone every attempt runs in. Spell it with its exact case (`Europe/Berlin`), or it is `INVALID_CONFIG`. |
| `InitScripts` | Scripts every document runs before the page's own (topic `writing-tests`). |
| `Connect` | Attach to a remote Chromium over CDP. |

To test without a browser or model, override `CreateEngine()` to return a
`DocumentEngine`, and `CreateModel()` to return a scripted `IAgentModel`.

## Environment variables

| Variable | Effect |
| --- | --- |
| `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, ... | The provider's key, or the variable `apiKeyEnv` names. |
| `E2E_USER_<NAME>_USERNAME`, `E2E_USER_<NAME>_PASSWORD` | The account `Credentials.User(name)` returns; the name is uppercased and `-` becomes `_`. |
| `E2E_SECRET_<NAME>` | Overrides `secrets.<name>`. |
| `E2E_OAUTH_CREDENTIALS` | The login JSON, in place of the credentials file. |
| `CI` | CI defaults: a `read-only` cache. |
| `E2E_HEADLESS` | `0` or `false` shows the browser. |
| `E2E_SKIP_BROWSER_INSTALL` | `1` skips the Chromium install. |

## Mobile targets

Not ported. There is no device engine.

## Done when

- `dotnet test` passes against the app.
- `.gitignore` lists `.e2e/`, or everything under it but `.e2e/cache/` to
  commit replays.
- CI runs the suite on pull requests; see topic `running`.
