// Copyright 2026 TesterArmy.
// Modified by Dario Kondratiuk.
// SPDX-License-Identifier: Apache-2.0

using E2E.Engine;
using E2E.Internal;

namespace E2E.Tests.ReadNodeChecked;

[Collection(BrowserCollection.Name)]
public sealed class ACheckableControlsValueTests(ChromiumPage chromium) : IClassFixture<ChromiumPage>
{
    [Fact]
    public async Task Reads_the_value_attribute_on_by_default_whatever_the_checked_state_as_Playwrights_inputValue_does()
    {
        await chromium.Page.SetContentAsync("""
            <input type="checkbox" data-testid="default-on" checked aria-label="Default on">
            <input type="checkbox" data-testid="default-off" aria-label="Default off">
            <input type="checkbox" data-testid="custom" value="yes" aria-label="Custom">
            <input type="radio" name="plan" data-testid="radio" value="monthly" aria-label="Monthly">
            <input type="radio" name="plan" data-testid="radio-default" aria-label="Default radio">
            """);
        var testIds = new[] { "default-on", "default-off", "custom", "radio", "radio-default" };
        var values = new List<string?>();
        foreach (var testId in testIds)
        {
            values.Add((await chromium.ReadAsync(chromium.Page.GetByTestId(testId)))?.Value);
        }

        Assert.Equal(["on", "on", "yes", "monthly", "on"], values);
        for (var index = 0; index < testIds.Length; index++)
        {
            Assert.Equal(values[index], await chromium.Page.GetByTestId(testIds[index]).InputValueAsync());
        }

        // The tree the model reads drops the token, as it drops an option's value; the checked state says what
        // matters. Here a locator read keeps the value on the node, and the snapshot is the model's tree.
        var nodes = await chromium.CaptureAsync();
        var snapshot = SnapshotText.Render(new Observation { Route = "/", Roots = nodes.Select(node => node.ToSemanticNode()).ToList() }, Redactor.None);
        Assert.Equal(testIds.Length, snapshot.Split('\n').Count(line => line.Contains("- checkbox", StringComparison.Ordinal) || line.Contains("- radio", StringComparison.Ordinal)));
        Assert.DoesNotContain("value=", snapshot, StringComparison.Ordinal);
    }
}
