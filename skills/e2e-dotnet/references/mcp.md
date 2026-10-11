# Driving the app over MCP

`e2e mcp` serves an e2e project to a coding agent over MCP (stdio). The
coding agent will drive the app the way the testing agent does: look at a
screen before writing a test, check a locator before committing to it.
Running tests and reading a failed run stay with `dotnet test`.

**In the .NET port** a session drives the one target of `e2e.config.json` in a
Chromium browser. The catalog is the testing agent's own actions plus `locate`
and `screenshot`. Not ported: recording a video, the project's own tools,
and the tap, hover, and type at a point. `e2e init` does not exist, so
register the server yourself (below).

## Setup

The server ships with the `E2E.Cli` tool (`dotnet tool install --global E2E.Cli`).
Register it with the client:

```bash
claude mcp add e2e -- e2e mcp   # Claude Code
```

Or declare it in the client's project config (`.mcp.json` for Claude Code,
`.cursor/mcp.json` for Cursor, `.vscode/mcp.json` for VS Code):

```json
{ "mcpServers": { "e2e": { "command": "e2e", "args": ["mcp"] } } }
```

In a clone of the e2e-dotnet repo, run it as
`dotnet run --project src/E2E.Cli -- mcp`.

Flags: `--config <path>` names the default config file, `--target <name>`
fixes the target every session opens on, `--headed` shows the browser in
sessions that do not set `headed` (sessions are headless by default),
`--max-sessions <n>` sets how many sessions may be open at once (default 4,
1 through 16). A bad value exits with code 2. The server exits with 0 when
the client disconnects or on Ctrl+C or `SIGTERM`, and with 1 on an
unexpected error. Stdout carries only the protocol; diagnostics go to
stderr.

## Tools

Four tools; everything a session can do is a catalog behind `call`.

| Tool | Does |
| --- | --- |
| `open_session` | Loads the config (`config` names another file; default the nearest `e2e.config.json`), boots the browser, opens the app URL, and returns the session id, the catalog, and the first observation. `target` must name the config's target when given. `headed: true` shows the browser when the user wants to watch. |
| `tools` | The catalog: one line per tool with its argument names (`?` marks optional), the first sentence of its description, and `[read-only]` where it changes nothing. `tools {tool}` shows the full description and the JSON Schema of its arguments. |
| `call` | Runs one catalog tool: `call {tool: "tap", args: {target: "e12"}}`, where `e12` is the `ref=` of a node in the newest observation. Arguments are checked against the tool's schema first; a wrong one fails with `INVALID_ARGUMENT` naming the field. |
| `close_session` | Ends the session and disposes the browser. With no session open and no `session` argument, it says `No session is open.` |

Every fixed tool takes only the arguments it declares. An unknown one fails
before anything runs, with `Invalid arguments for tool <name>: Unrecognized
key: "<key>"`.

Resources: `e2e://guide` is the skill overview, and `e2e://guide/<topic>`
holds one topic (`e2e guide <topic>` prints the same text). An unknown topic fails with
`UNKNOWN_TOPIC`.

## The catalog

`observe` shows the whole screen, one node per line with its `[ref=...]`. Every
action returns the screen after it. What an engine lacks is missing from its
catalog: `scroll` and `scroll_to` need scrolling, `back` needs history,
`screenshot` needs the web engine, and `type_secret` appears only when the
config declares a `secrets` entry.

| Tool | Does |
| --- | --- |
| `observe` | The whole current screen |
| `tap`, `double_tap`, `check`, `uncheck`, `clear` `{target}` | Act on one node |
| `type {target, text}` | Replace the text in a field |
| `type_secret {target, secret}` | Fill a configured secret into an editable field by name; you never see the value |
| `press {key, target?}` | Press a key on a node, or on whatever has focus |
| `select {target, value}` | Choose an option |
| `scroll {direction, times?, target?}`, `scroll_to {target?, text?, direction?}` | Move the viewport, or page to a node |
| `navigate {url}` | Open a path or an http or https URL. Any other scheme fails with `POLICY_DENIED` |
| `back` | One step back in the history |
| `screenshot` | The viewport as an image. After a secret was filled it answers `PIXEL_TAINTED` and attaches nothing |
| `locate {role? name? text? label? placeholder? testId? exact?}` | Try a locator; see the loop below |

A failed action is an error result that starts with the tool, the node, and
the code: `tap n9999 failed: NOT_FOUND: ...`.

## Workflow

The loop is:

1. `open_session`, then `call {tool: "observe"}` and act until the screen you
   want to test is in front of you. Node refs are valid only for the newest
   observation.
2. `call {tool: "locate", args: {...}}` for each locator you intend to write.
   One match: use the printed `Screen.GetByRole(...)` call. Zero or several:
   adjust before writing the test; the same failure would hit the test as
   `LOCATOR_NOT_FOUND` or `STRICT_MODE`.
3. Write an `E2ETest` class (topic `writing-tests`). Deterministic steps where
   you saw exact names; `Agent.ActAsync` where the flow varies.
4. Run it from the shell: `dotnet test --filter "FullyQualifiedName~<Class>"`,
   read the failure, fix, repeat.
5. `close_session` when you are done.

## Rules

- A session ends when you close it, after 30 minutes without a call, or after
  4 hours. A secret value never leaves the server: every output shows it as
  `<secret:name>`.
- Nothing a session does is recorded as a test or into the replay cache. A
  session is for looking and trying; the test is what you write afterwards.
- Parallel agents (subagents) share one server: each opens its own session
  and passes its session id to every `tools`, `call`, and `close_session`.
  A call may leave `session` out only while one session is open.
- `NO_SESSION`: call `open_session` first, or the session named has ended
  (the message says why). `SESSION_REQUIRED`: several sessions are open;
  pass `session`. `SESSION_OPEN`: all slots are taken (`--max-sessions`).
  `CONFIG_IN_USE`: sessions open at once share one config. `UNKNOWN_TOOL`: the name is not in this session's catalog;
  the message lists what is.
