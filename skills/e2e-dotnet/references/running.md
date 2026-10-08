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
entries: a secret an act received in `Params` that shows in a control's name,
text, value, placeholder, or test id is stored as `<secret:name>`, and the
replay matches the screen in that same redacted form.

An entry belongs to a test, an instruction, its params, and an agent: its
name and a hash of its redacted `context`. Running with another agent, or
changing the agent's `context`, records again. Upgrading to a version that
changes how entries replay re-keys every entry, so the first run after it
records them again.

`cache.strict` fails a step whose committed recording no longer replays
(`REPLAY_STALE`) instead of handing it to the agent. That includes a
`no-entry` miss while the cache directory holds a recording of the same
step under another key, for example after an `E2E` upgrade or a change
to the agent's `context` re-keyed it. The message names that file. A custom `IStepCache` is not
checked this way. A step never recorded (a new or edited instruction or
params) and a retry still run live. Re-record with a `read-write` run
without `cache.strict`, commit the changed entry, and delete the old file
once nothing replays it.

Each entry records the step it was made for: the test title, the engine,
the instruction, a params digest, the repeat index, and the agent name. A
secret value in the test title or agent name is masked before it is
stored. Entries recorded before these fields existed count once a
`read-write` run replays them.

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
