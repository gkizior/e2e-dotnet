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
- Every test ports an upstream `it`. Do not keep a test that has no upstream `it`, even for .NET-only code. If an upstream `it` covers the behavior, port that `it` instead.
- A ported test checks everything its upstream `it` checks. If a part needs a feature the port does not have, name it in the PR report. Do not keep an upstream name on a test that checks only part of it.

## Before you push

- If the change can affect the agent, run every real-model test: `dotnet test E2E.slnx --filter "TestCategory=RealModel"`. This covers the agent, prompts, cache, replay, the web engine, and the page script. Doc-only and config-only changes skip this step.
- The tests use the GitHub Copilot login (`dotnet run --project src/E2E.Cli -- login github-copilot`). If you cannot log in, say so in your report. Do not skip the tests silently.
- Real-model tests can be flaky. Rerun a failed test once. If it fails twice, treat it as a real failure and report it.
