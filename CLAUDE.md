# e2e-dotnet

This repo is a .NET port of [tester-army/e2e](https://github.com/tester-army/e2e). See CONTRIBUTING.md and COMPATIBILITY.md.

## Porting an upstream change

- Port every test that the upstream commit adds or changes. Keep its name, steps, and assertions.
- A test that needs a real model or a testbed page is still a test to port. Port the page too. Run the test as a real-model test (`[Category("RealModel")]`, like `samples/TodoMvc`).
- When the port adds or changes public API or user-visible behavior, update `skills/e2e-dotnet/SKILL.md` and `docs/` in the same PR. Mirror what upstream's `skills/e2e/references/*.md` and `docs/**/*.mdx` say about that member, in the port's C# names.
- Skip an upstream test only when the port has no such feature at all (for example, tracing). Name each skipped test and the reason in the PR report.
- Lay out ported tests like upstream:
  - Each upstream test file is a directory, named in PascalCase. `protected-app-options.test.ts` becomes `ProtectedAppOptions/`.
  - Each `describe` in that file is one test class file inside the directory. `describe('web({ locale, timezoneId })')` becomes `ProtectedAppOptions/WebLocaleTimezoneIdTests.cs`.
  - Each `it` is one test method, named after the `it` text.
  - A class holds every `it` of its upstream `describe`, not only the ones the commit adds.
  - Tests outside any `describe` go in `<Directory>/<Directory>Tests.cs`.
  - The directory sits in the test project that runs the test, and the namespace follows it: `E2E.Tests.ProtectedAppOptions`.
- In the test projects under `tests/`, every test ports an upstream `it`, except as the next rule allows. If an upstream `it` covers the behavior, even in part, port that `it` instead of writing your own test.
- .NET-only code may keep tests that have no upstream `it`, when no upstream `it` covers that behavior. Examples: model providers, the SigV4 signer, launch timeouts, adapters. Name each such test in the PR report.
- The sample tests under `samples/` (the TodoMvc and Dariten samples) have no upstream `it`. They are required. Do not remove them.
- A ported test checks everything its upstream `it` checks. If a part needs a feature the port does not have, name it in the PR report. Do not keep an upstream name on a test that checks only part of it.
- A port follows upstream's structure and names. Every new type, method, field, constant, or file in `src/` must map to an upstream function, class, or constant (in C# casing). It lives in the equivalent file, with the same owner, and keeps upstream's split of responsibilities.
  - Before you add a symbol, find its upstream counterpart. Read tester-army/e2e with `gh api`. Reuse its name and place.
  - A name upstream does not have is allowed only for a .NET-only concern (dependency injection, nullable handling, test fixtures, and the .NET-only code listed above, such as model providers and the SigV4 signer). An `Async` suffix is fine. The PR report names each such symbol and the reason.
  - Example: PR #249 first added `PerformTargetedAsync`. Upstream has `commitTargeted` calling a private `targeted` in `agent/action-dispatcher.ts`, and `feed.relocate` on the observation feed. The port uses those.

## Before you push

- If the change can affect the agent, run every real-model test: `dotnet test E2E.slnx --filter "TestCategory=RealModel"`. This covers the agent, prompts, cache, replay, the web engine, and the page script. Doc-only and config-only changes skip this step.
- The tests use the GitHub Copilot login (`dotnet run --project src/E2E.Cli -- login github-copilot`). If you cannot log in, say so in your report. Do not skip the tests silently.
- Real-model tests can be flaky. Rerun a failed test once. If it fails twice, treat it as a real failure and report it.
