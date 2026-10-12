// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

namespace E2E.Engine;

/// <summary>Separates exceptions thrown by trusted page code from failures of the evaluation transport.</summary>
internal static class Evaluation
{
    /// <summary>
    /// Compiles the page-side error boundary around one expression: a function it evaluates to is called with the
    /// argument, any other value is the result. The page code runs inside a try, so an exception it throws comes
    /// back as data, apart from the browser's own failures.
    /// </summary>
    public static string CompileEvaluation(string expression, bool hasArg) => $$"""
        async (arg) => {
          try {
            return { ok: true, value: await ((result) => typeof result === 'function' ? result({{(hasArg ? "arg" : "")}}) : result)(({{expression}}
        )) };
          } catch (cause) {
            let message;
            try {
              message = typeof cause?.message === 'string' ? cause.message : String(cause);
            } catch {
              message = 'Page evaluation threw an unprintable value';
            }
            return { ok: false, message };
          }
        }
        """;
}
