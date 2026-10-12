// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Cli.Mcp;

namespace E2E.Cli.Tests.SecretLedger;

/// <summary>
/// The MCP server's ledger, as upstream's <c>SecretLedger</c>. A secret in the port has at least 6
/// code points, so the values differ from upstream's shorter ones.
/// </summary>
public sealed class SecretLedgerTests
{
    [Fact]
    public void Redacts_values_registered_after_the_redact_function_was_handed_out()
    {
        var ledger = new E2E.Internal.SecretLedger();
        ledger.Add([Secret.Create("member", "hunter2")]);
        Func<string, string> redact = ledger.Redact;
        Assert.Equal("pw is <secret:member>", redact("pw is hunter2"));
        ledger.Add([Secret.Create("totp", "123456")]);
        Assert.Equal("code <secret:totp> for <secret:member>", redact("code 123456 for hunter2"));
    }

    [Fact]
    public void Keeps_redacting_earlier_values_after_a_name_rotates()
    {
        var ledger = new E2E.Internal.SecretLedger();
        ledger.Add([Secret.Create("totp", "111111")]);
        ledger.Add([Secret.Create("totp", "222222")]);
        Assert.Equal("first <secret:totp> then <secret:totp>", ledger.Redact("first 111111 then 222222"));
    }

    [Fact]
    public void Keeps_the_MCP_call_boundary_from_rewriting_what_observe_already_redacted()
    {
        var ledger = new E2E.Internal.SecretLedger();
        ledger.Add([Secret.Create("apiKey", "apiKey")]);
        var observed = Tools.TextResult("#n3 textbox \"Token\" value=\"<secret:apiKey>\"");
        var redacted = SessionHost.RedactResult(observed, ledger.Redact);
        Assert.Equal(
            [("text", "#n3 textbox \"Token\" value=\"<secret:apiKey>\"")],
            McpTools.ToolFixtures.Parts(redacted));
    }
}
