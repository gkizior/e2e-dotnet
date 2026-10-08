# Driving the app over MCP

`e2e mcp` serves an e2e project to a coding agent over MCP (stdio). The
coding agent will drive the app the way the testing agent does: look at a
screen before writing a test, check a locator before committing to it.
Running tests and reading a failed run stay with `dotnet test`.

**Status in the .NET port.** The server, its four fixed tools, and the guide
resources work. Live sessions do not exist yet: `open_session` answers
`UNSUPPORTED_CAPABILITY`, and `tools`, `call`, and `close_session` answer as
they do with no session open. Until sessions land, read the guide resources
and write tests from the app's markup and `Screen` locators (topic
`writing-tests`).

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
| `open_session` | Loads the config (`config` names another file; default the nearest `e2e.config.json`), boots the browser, opens the app URL, and returns the session id, the catalog, and the first observation. `target` is required when the config declares several. `headed: true` shows the browser when the user wants to watch. Not ported yet: it answers `UNSUPPORTED_CAPABILITY`. |
| `tools` | The catalog: one line per tool with its argument names (`?` marks optional), the first sentence of its description, and `[read-only]` where it changes nothing. `tools {tool}` shows the full description and the JSON Schema of its arguments. |
| `call` | Runs one catalog tool: `call {tool: "tap", args: {target: "n42"}}`. Arguments are checked against the tool's schema first; a wrong one fails with `INVALID_ARGUMENT` naming the field. |
| `close_session` | Ends the session and disposes the browser. With no session open and no `session` argument, it says `No session is open.` |

Every fixed tool takes only the arguments it declares. An unknown one fails
before anything runs, with `Invalid arguments for tool <name>: Unrecognized
key: "<key>"`.

Resources: `e2e://guide` is the skill overview, and `e2e://guide/<topic>`
holds one topic (`e2e guide <topic>` prints the same text). An unknown topic fails with
`UNKNOWN_TOPIC`.

## Workflow

Once sessions land, the loop is:

1. `open_session`, then `call {tool: "observe"}` and act until the screen you
   want to test is in front of you. Node ids are valid only for the newest
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

- Nothing a session does is recorded as a test or into the replay cache. A
  session is for looking and trying; the test is what you write afterwards.
- Parallel agents (subagents) share one server: each opens its own session
  and passes its session id to every `tools`, `call`, and `close_session`.
  A call may leave `session` out only while one session is open.
- `NO_SESSION`: call `open_session` first, or the session named has ended
  (the message says why). `SESSION_REQUIRED`: several sessions are open;
  pass `session`. `UNKNOWN_TOOL`: the name is not in this session's catalog;
  the message lists what is.
