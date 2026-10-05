# Contributing

This repo is a community .NET port of [tester-army/e2e](https://github.com/tester-army/e2e). Prefer the upstream public behavior, described in [COMPATIBILITY.md](COMPATIBILITY.md), over a .NET-only invention. Record intentional differences in that file.

## Layout

| Path | Role |
| --- | --- |
| `src/E2E` | SDK: tests, expect, agent, cache, document engine, Playwright `WebEngine` |
| `src/E2E.NUnit` | NUnit fixture `E2ETest` |
| `tests/E2E.Tests` | Unit tests. No API key. The Chromium tests install the browser once per run |
| `samples/E2E.Sample` | NUnit tests against the live Dariten demo. The agent test needs a GitHub Copilot login (`dotnet run --project src/E2E.Cli -- login github-copilot`) |

## Development

Requires the .NET 10 SDK (`global.json`).

```bash
dotnet restore
dotnet build
dotnet test
```

The Chromium tests in `tests/E2E.Tests` install the Chromium build that Playwright expects once per run, through `BrowserFixture`. They need network access the first time, and fail if the install fails.

Style is enforced at build time through `.editorconfig` and `Directory.Build.props` (`EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`). C# files use the Apache file header.

Do not commit API keys.

## Release

Push a version tag such as `v0.1.0` (previews use `v0.1.0-preview.1`). `.github/workflows/publish.yml` tests, packs `E2E` and `E2E.NUnit`, and pushes them to nuget.org with trusted publishing. The policy is repository owner `hardkoded`, repository `e2e-dotnet`, workflow file `publish.yml`. The workflow has to be on the default branch before the tag is pushed.
