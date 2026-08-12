using Microsoft.Playwright;
using ShanthiNikethan.EmployeeManagement.E2ETests.TestHelpers;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.E2ETests;

public class AuditLogTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    public AuditLogTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _page = await _fixture.NewIsolatedPageAsync();

    public async Task DisposeAsync() => await _page.Context.CloseAsync();

    /// <summary>
    /// Audit Log sits under the collapsible "Administration" nav group,
    /// not as a direct sidebar item - and given the real, hard lesson
    /// from the Payroll module this session, direct GotoAsync navigation
    /// right after sign-in is avoided here too, in favor of the
    /// already-proven click-based approach.
    /// </summary>
    private async Task NavigateToAuditLogAsync()
    {
        await _page.GetByText("Administration", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await _page.GetByText("Audit Log", new PageGetByTextOptions { Exact = true }).ClickAsync();
    }

    [Fact]
    public async Task DefaultView_ShowsRealAccumulatedActivity()
    {
        // Deliberately no setup - this whole session has generated a
        // large amount of genuine activity (staff created, signed in,
        // payroll runs, leave records...), so the default "last 7 days"
        // view should show real results without needing to manufacture
        // any test data first.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAuditLogAsync();

        await Assertions.Expect(_page.GetByText("No activity matches these filters.")).Not.ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("table.grid tbody tr").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ClickingARow_OpensTheDetailDrawerOnBasicInfo()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAuditLogAsync();

        await _page.Locator("table.grid tbody tr").First.ClickAsync();

        // Drawer opens straight to Basic Info (confirmed in OpenDrawer -
        // always resets to this tab regardless of what was open last).
        await Assertions.Expect(_page.GetByText("When", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Role group at the time")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DrawerTabs_SwitchingToLocationAndDevice_ShowsDifferentContent()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAuditLogAsync();
        await _page.Locator("table.grid tbody tr").First.ClickAsync();

        await _page.GetByText("Location & Device").ClickAsync();

        // Scoped to .drawer specifically - the table's own column
        // headers use this exact same text ("IP Address", "Geo-location"),
        // and remain in the DOM behind the drawer overlay rather than
        // being removed, so an unscoped match would be genuinely
        // ambiguous between the drawer's label and the table header.
        var drawer = _page.Locator(".drawer");
        await Assertions.Expect(drawer.GetByText("IP address")).ToBeVisibleAsync();
        await Assertions.Expect(drawer.GetByText("Geo-location")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CustomDateRange_RevealsTheFromAndToInputs()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAuditLogAsync();

        // Not present under the default "Last 7 days" preset.
        await Assertions.Expect(_page.Locator("input[type='date']")).ToHaveCountAsync(0);

        await _page.GetByText("Custom range").ClickAsync();

        await Assertions.Expect(_page.Locator("input[type='date']")).ToHaveCountAsync(2);
    }

    [Fact]
    public async Task KeywordSearch_ForANonsenseTerm_NarrowsResultsToZero()
    {
        // A guaranteed-unique, nonsense keyword rather than a real one -
        // this doesn't depend on knowing what data actually exists, only
        // that a term matching nothing at all genuinely produces zero
        // results once applied, proving the filter has real effect.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAuditLogAsync();

        var nonsenseKeyword = $"E2E-NoMatch-{Guid.NewGuid()}";
        await _page.GetByPlaceholder("Search details…").FillAsync(nonsenseKeyword);
        await _page.GetByText("Apply filters", new PageGetByTextOptions { Exact = false }).ClickAsync();

        await Assertions.Expect(_page.GetByText("No activity matches these filters.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ClearAll_ResetsAKeywordFilterAndShowsResultsAgain()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAuditLogAsync();

        var nonsenseKeyword = $"E2E-NoMatch-{Guid.NewGuid()}";
        var keywordField = _page.GetByPlaceholder("Search details…");
        await keywordField.FillAsync(nonsenseKeyword);
        await _page.GetByText("Apply filters", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await Assertions.Expect(_page.GetByText("No activity matches these filters.")).ToBeVisibleAsync();

        await _page.GetByText("Clear all").ClickAsync();

        // The field itself is empty again...
        await Assertions.Expect(keywordField).ToHaveValueAsync("");
        // ...and real results are showing again, not still filtered out.
        await Assertions.Expect(_page.GetByText("No activity matches these filters.")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task DrawerTabs_SwitchingToAuthenticationDetails_ShowsDifferentContent()
    {
        // The third and last drawer tab - Basic Info and Location &
        // Device were covered separately; this one was untested entirely.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAuditLogAsync();
        await _page.Locator("table.grid tbody tr").First.ClickAsync();

        await _page.GetByText("Authentication Details").ClickAsync();

        var drawer = _page.Locator(".drawer");
        await Assertions.Expect(drawer.GetByText("Provider")).ToBeVisibleAsync();
        await Assertions.Expect(drawer.GetByText("Request ID")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ModuleCheckboxFilter_NarrowsOrMaintainsTheResultCount()
    {
        // The Module/Entity Type/Action filters use checkboxes, a
        // genuinely different interaction pattern than the free-text
        // Keyword filter already covered - and their exact available
        // values are dynamic, populated from whatever real data exists,
        // so this can't assert a specific value. Module is the first
        // checkbox group in DOM order (Date Range above it uses radios,
        // not checkboxes), so its first checkbox is reliably reachable
        // by position without knowing its label ahead of time. Narrowing
        // by any single module should never increase the total count -
        // that's the one thing provable without knowing exact data.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAuditLogAsync();

        var totalCountText = _page.Locator(".toolbar .stat-sub").First;
        var beforeCount = ParseLeadingNumber(await totalCountText.InnerTextAsync());

        var firstModuleCheckbox = _page.Locator("input[type='checkbox']").First;
        await firstModuleCheckbox.CheckAsync();
        await _page.GetByText("Apply filters", new PageGetByTextOptions { Exact = false }).ClickAsync();

        var afterText = await _page.Locator(".toolbar .stat-sub").First.InnerTextAsync();
        // Either a real count, or the empty-state message replaces the
        // toolbar count entirely when a filter narrows results to zero -
        // both are valid outcomes of "never increased".
        if (afterText.Contains("result"))
        {
            var afterCount = ParseLeadingNumber(afterText);
            Assert.True(afterCount <= beforeCount, $"Expected filtered count ({afterCount}) to be no greater than unfiltered count ({beforeCount}).");
        }
        else
        {
            await Assertions.Expect(_page.GetByText("No activity matches these filters.")).ToBeVisibleAsync();
        }
    }

    private static int ParseLeadingNumber(string text)
    {
        var digits = new string(text.TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out var n) ? n : 0;
    }
}
