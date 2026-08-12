using Microsoft.Playwright;
using ShanthiNikethan.EmployeeManagement.E2ETests.TestHelpers;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.E2ETests;

public class AutomationRulesTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    public AutomationRulesTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _page = await _fixture.NewIsolatedPageAsync();

    public async Task DisposeAsync() => await _page.Context.CloseAsync();

    /// <summary>
    /// GraphDiagnostics.razor (linked from this page's own warning banner)
    /// is deliberately never touched anywhere in this file. Every button
    /// on it triggers a real, live Entra API call - creating an actual
    /// user, disabling/deleting/restoring real accounts, real group
    /// membership changes. Not a coverage gap - a deliberate boundary,
    /// same class of risk as IdentityProvider.
    /// </summary>
    private async Task NavigateToAutomationRulesAsync()
    {
        await _page.GetByText("Administration", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await _page.GetByText("Automation Rules", new PageGetByTextOptions { Exact = true }).ClickAsync();
    }

    private async Task<string> CreateRuleAsync()
    {
        var name = $"E2E Rule {(uint)Guid.NewGuid().GetHashCode()}";
        await _page.GetByText("Create rule", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await _page.GetByPlaceholder("e.g. Assign License").FillAsync(name);
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        // A fake-but-valid-looking GUID - this field is stored as plain
        // text, never validated against a real Entra group, confirmed
        // directly in CreateRuleAsync's own implementation.
        await _page.GetByPlaceholder("e.g. 3fa85f64-5717-4562-b3fc-2c963f66afa6").FillAsync(Guid.NewGuid().ToString());
        await _page.Keyboard.PressAsync("Tab");
        await _page.Locator(".modal-actions").GetByText("Save", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();
        return name;
    }

    [Fact]
    public async Task CreateRule_HappyPath_AppearsInTheListAsEnabled()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAutomationRulesAsync();

        var name = await CreateRuleAsync();

        var row = _page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = name });
        await Assertions.Expect(row).ToBeVisibleAsync();
        await Assertions.Expect(row.GetByText("Enabled", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CreateRule_WithoutAName_ShowsAnInlineError()
    {
        // A genuinely different validation pattern than most other
        // modules this session - this page validates AFTER clicking
        // Save with an inline error, rather than disabling the button
        // ahead of time.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAutomationRulesAsync();

        await _page.GetByText("Create rule", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await _page.GetByPlaceholder("e.g. 3fa85f64-5717-4562-b3fc-2c963f66afa6").FillAsync(Guid.NewGuid().ToString());
        await _page.Keyboard.PressAsync("Tab");
        await _page.Locator(".modal-actions").GetByText("Save", new LocatorGetByTextOptions { Exact = true }).ClickAsync();

        await Assertions.Expect(_page.GetByText("Rule name is required.")).ToBeVisibleAsync();
        // Still open - the save was genuinely rejected, not just slow.
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CreateRule_WithoutAnEntraGroupId_ShowsAnInlineError()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAutomationRulesAsync();

        await _page.GetByText("Create rule", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await _page.GetByPlaceholder("e.g. Assign License").FillAsync($"E2E No Group ID {(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Keyboard.PressAsync("Tab");
        await _page.Locator(".modal-actions").GetByText("Save", new LocatorGetByTextOptions { Exact = true }).ClickAsync();

        await Assertions.Expect(_page.GetByText("Entra group Object ID is required.")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task EditRule_DisablingIt_UpdatesTheStatusBadge()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAutomationRulesAsync();
        var name = await CreateRuleAsync();

        var row = _page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = name });
        await row.ClickAsync();

        // The "Enabled" checkbox only renders in edit mode, confirmed
        // directly in the code - never present when creating.
        await _page.Locator(".modal input[type='checkbox']").UncheckAsync();
        await _page.Locator(".modal-actions").GetByText("Save", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        await Assertions.Expect(row.GetByText("Disabled", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DeleteRule_NeverApplied_Succeeds()
    {
        // The safeguard against deleting a rule that's already been
        // applied to a staff member is deliberately NOT tested here -
        // reaching that state would mean actually assigning someone to a
        // real Entra group during Add Staff, the same Layer-3 boundary
        // GraphDiagnostics represents. Only the safe, never-used path is
        // covered.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAutomationRulesAsync();
        var name = await CreateRuleAsync();

        var row = _page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = name });
        await row.ClickAsync();
        await _page.Locator(".modal-actions").GetByText("Delete", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.GetByText("Delete this rule?")).ToBeVisibleAsync();
        await _page.Locator(".modal-actions").GetByText("Delete permanently").ClickAsync();

        await Assertions.Expect(_page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = name })).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task EditRule_ChangingNameAndDescription_PersistsInTheList()
    {
        // Genuinely different from EditRule_DisablingIt above - that one
        // only toggled the Enabled checkbox; this exercises
        // UpdateRuleAsync with actually-changed text field values.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAutomationRulesAsync();
        var originalName = await CreateRuleAsync();

        var row = _page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = originalName });
        await row.ClickAsync();

        var newName = $"E2E Renamed Rule {(uint)Guid.NewGuid().GetHashCode()}";
        await _page.GetByPlaceholder("e.g. Assign License").FillAsync(newName);
        await _page.Keyboard.PressAsync("Tab");
        await _page.GetByPlaceholder("Your own notes — not shown to Office Admins").FillAsync("E2E test description");
        await _page.Keyboard.PressAsync("Tab");
        await _page.Locator(".modal-actions").GetByText("Save", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        var renamedRow = _page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = newName });
        await Assertions.Expect(renamedRow).ToBeVisibleAsync();
        await Assertions.Expect(renamedRow.GetByText("E2E test description")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task EditRule_ClickingCancel_DiscardsTheChange()
    {
        // Confirms Cancel genuinely discards rather than silently
        // saving - checked by attempting a rename, canceling, then
        // confirming the ORIGINAL name is still what's shown, not just
        // that a new row wasn't created.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAutomationRulesAsync();
        var originalName = await CreateRuleAsync();

        var row = _page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = originalName });
        await row.ClickAsync();
        await _page.GetByPlaceholder("e.g. Assign License").FillAsync($"E2E Should Not Save {(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Keyboard.PressAsync("Tab");
        await _page.Locator(".modal-actions").GetByText("Cancel").ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        await Assertions.Expect(_page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = originalName })).ToBeVisibleAsync();
    }
}
