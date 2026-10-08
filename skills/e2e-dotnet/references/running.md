# Running tests

## Commands

Tests run with the .NET test runner. The `e2e` tool holds the rest:

```bash
dotnet test                                          # run every test
dotnet test --filter "FullyQualifiedName~BillingTests"  # one class
dotnet test --filter "TestCategory!=RealModel"       # skip tests that call a model
e2e guide [topic]                                    # print this skill; topics: agent, bug-bash, debugging,
                                                     # explore, mcp, running, setup, writing-tests,
                                                     # writing-tests-nunit, writing-tests-xunit
e2e login|logout|models [provider]                   # subscription logins: openai,
                                                     # github-copilot, opencode-console, spacexai
e2e mcp                                              # MCP server for a coding agent (topic mcp)
```

Selection, retries, and parallelism are the test framework's: `--filter`
(by name, class, or category), NUnit `[Retry]`, `[Category]`, and
`[Parallelizable]`, or xUnit v3 traits. There is no `e2e run`, `e2e list`,
`e2e explore`, `e2e init`, `e2e cache`, or `e2e feedback`.

To watch a run, set `E2E_HEADLESS=0`. A fixture can also pass
`new WebEngineOptions { Headless = false }` in `CreateEngine()`.

## The replay cache

Entries live under `cache.dir` (`.e2e/cache/` by default), one file per key,
relative to the config file. There are no `cache` commands; delete the
directory to clear it. The modes are `read-write`, `read-only`, and `off`.
Topic `agent` explains what is recorded. Secret values never enter cache
entries: a secret an act received in `Params` that shows in a control's name
or test id is stored as `<secret:name>`, and the replay matches the screen in
that same redacted form.

## Output

The port writes no report, trace page, video, or `junit.xml` of its own.
Use the test runner's loggers, such as
`dotnet test --logger "trx;LogFileName=e2e.trx"` or
`--logger "console;verbosity=detailed"`.

## Exit codes

`dotnet test` exits 0 when every test passed or was skipped, and 1
otherwise. The `e2e` tool exits 0 on success and 1 on an error or an unknown
command. `e2e guide` exits 2 on an unknown topic, and `e2e mcp` exits 2 on a
bad flag.

## Continuous integration

CI mode is on when `CI` is set (not `0` or `false`): the replay cache is
`read-only` unless the config sets a mode. Nothing retries on its own; use
NUnit `[Retry]` for that.

```yaml
# .github/workflows/e2e.yml
name: e2e
on:
  pull_request:
  push:
    branches: [main]
permissions:
  contents: read
jobs:
  e2e:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - run: dotnet build -c Release
      - run: pwsh MyApp.E2E/bin/Release/net10.0/playwright.ps1 install --with-deps chromium
      - run: dotnet test -c Release --no-build --logger "trx;LogFileName=e2e.trx"
        env:
          E2E_SKIP_BROWSER_INSTALL: '1'
          OPENAI_API_KEY: ${{ secrets.OPENAI_API_KEY }}
          E2E_USER_ADMIN_USERNAME: ${{ secrets.E2E_USER_ADMIN_USERNAME }}
          E2E_USER_ADMIN_PASSWORD: ${{ secrets.E2E_USER_ADMIN_PASSWORD }}
```

- Install the browser in its own step, so the download stays out of the
  launch timeout.
- Start the app before `dotnet test`; the port has no `app.command`.
- Agent steps run in the same job: pass the provider's API key from the CI
  secrets, and commit `.e2e/cache/` so recorded acts replay with no model
  call. Subscriptions are for local runs.
