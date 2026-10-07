# Contributing

This repo is a community .NET port of [tester-army/e2e](https://github.com/tester-army/e2e). Prefer the upstream public behavior, described in [COMPATIBILITY.md](COMPATIBILITY.md), over a .NET-only invention. Record intentional differences in that file.

## Layout

| Path | Role |
| --- | --- |
| `src/E2E` | SDK: tests, expect, agent, cache, document engine, Playwright `WebEngine` |
| `src/E2E.NUnit` | NUnit fixture `E2ETest` |
| `src/E2E.Xunit` | xUnit v3 fixture `E2ETest` |
| `tests/E2E.Tests` | Unit tests. No API key. The Chromium tests install the browser on the first launch |
| `tests/E2E.Xunit.Tests` | Tests of the xUnit fixture. No API key |
| `tests/E2E.Agent.Tests` | Agent tests against a real model, ported from upstream's `apps/testbed/tests-agent`. They serve upstream's playground pages on a loopback port. All are in the `RealModel` category and need a GitHub Copilot login |
| `samples/E2E.Sample` | NUnit tests against the live Dariten demo. The agent test needs a GitHub Copilot login (`dotnet run --project src/E2E.Cli -- login github-copilot`) |
| `samples/E2E.Xunit.Sample` | The same tests on xUnit v3, through `E2E.Xunit`. The agent test needs the same login |

## Development

Requires the .NET 10 SDK (`global.json`).

```bash
dotnet restore
dotnet build
dotnet test
```

`WebEngine` installs the Chromium build that Playwright expects on its first launch in a process. So the Chromium tests in `tests/E2E.Tests` need network access the first time, and fail if the install fails. `E2E_SKIP_BROWSER_INSTALL=1` skips the install.

Style is enforced at build time through `.editorconfig` and `Directory.Build.props` (`EnforceCodeStyleInBuild`, `TreatWarningsAsErrors`). C# files use the Apache file header.

Do not commit API keys.

## Release

Push a version tag such as `v0.1.0` (previews use `v0.1.0-preview.1`). `.github/workflows/publish.yml` tests, packs `E2E`, `E2E.NUnit`, and `E2E.Xunit`, and pushes them to nuget.org with trusted publishing. The policy is repository owner `hardkoded`, repository `e2e-dotnet`, workflow file `publish.yml`. The workflow has to be on the default branch before the tag is pushed.

The same tag runs `.github/workflows/docs.yml`, which builds the DocFX site in `docs/` and deploys it to GitHub Pages. Build it locally with `dotnet tool install --global docfx`, then `docfx docs/docfx.json --serve`.
