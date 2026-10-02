# Contributing

This repo is a community .NET port of [tester-army/e2e](https://github.com/tester-army/e2e). Prefer the upstream public behavior, described in [COMPATIBILITY.md](COMPATIBILITY.md), over a .NET-only invention. Record intentional differences in that file.

## Layout

| Path | Role |
| --- | --- |
| `src/E2E` | SDK: tests, expect, agent, cache, document engine, Playwright `WebEngine` |
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

The Playwright test is skipped in-process when Chromium is missing. Install it with the `playwright.ps1` script in the `E2E` build output.

Style is enforced at build time through `.editorconfig` and `Directory.Build.props` (`EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`). C# files use the Apache file header.

Do not commit API keys.

## Release

Push a version tag such as `v0.1.0` (previews use `v0.1.0-preview.1`). `.github/workflows/publish.yml` tests, packs `E2E` and `E2E.Cli`, and pushes them to nuget.org with trusted publishing. The policy is repository owner `hardkoded`, repository `e2e-dotnet`, workflow file `publish.yml`. The workflow has to be on the default branch before the tag is pushed.
