# Contributing

This repo is a community .NET port of [tester-army/e2e](https://github.com/tester-army/e2e). Prefer the upstream public behavior, described in [COMPATIBILITY.md](COMPATIBILITY.md), over a .NET-only invention. Record intentional differences in that file.

## Layout

| Path | Role |
| --- | --- |
| `src/E2E` | SDK: tests, expect, agent, cache, document engine |
| `src/E2E.Web` | Playwright web engine |
| `src/E2E.Cli` | `e2e` tool |
| `tests/E2E.Tests` | Unit tests. No API key, no browser required |
| `samples/E2E.Sample` | Billing upgrade, twice, to show replay |

## Development

Requires the .NET 10 SDK (`global.json`).

```bash
dotnet restore
dotnet build
dotnet test
```

The Playwright test is skipped in-process when Chromium is missing. Install it with the `playwright.ps1` script in the `E2E.Web` build output.

Style is enforced at build time through `.editorconfig` and `Directory.Build.props` (`EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`). C# files use the Apache file header.

Do not commit API keys.
