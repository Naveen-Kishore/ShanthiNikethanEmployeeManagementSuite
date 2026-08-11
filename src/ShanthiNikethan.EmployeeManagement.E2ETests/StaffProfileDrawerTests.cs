using Microsoft.Playwright;
using ShanthiNikethan.EmployeeManagement.E2ETests.TestHelpers;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.E2ETests;

public class StaffProfileDrawerTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    public StaffProfileDrawerTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _page = await _fixture.NewIsolatedPageAsync();

    public async Task DisposeAsync() => await _page.Context.CloseAsync();

    /// <summary>
    /// Creates a real staff member via the already-proven Add Staff flow,
    /// then opens their profile drawer by clicking their name in the
    /// directory. Every test in this file needs a real profile to open,
    /// so this is the shared starting point.
    /// </summary>
    private async Task<string> CreateStaffMemberAndOpenDrawerAsync()
    {
        var uniqueNum = (uint)Guid.NewGuid().GetHashCode();
        var name = $"E2E Drawer Test {uniqueNum}";
        var tenDigits = uniqueNum.ToString("D10");
        var aadhaar = "99" + tenDigits;
        var phone = "9" + tenDigits.Substring(0, 9);
        var bankAccount = "E2E" + uniqueNum;

        await _page.GotoAsync($"{E2EConfig.BaseUrl}/staff");
        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await _page.Locator("div.form-row:has(label:text('Full name *')) input").FillAsync(name);
        await _page.Locator("div.form-row:has(label:text('Date of joining *')) input").FillAsync("2020-06-01");
        await _page.GetByPlaceholder("10-digit mobile").FillAsync(phone);
        await _page.GetByPlaceholder("12-digit number").FillAsync(aadhaar);
        await _page.GetByPlaceholder("Type account number").FillAsync(bankAccount);
        await _page.GetByPlaceholder("e.g. SBIN0001234").FillAsync("SBIN0001234");
        await _page.GetByPlaceholder("Must match exactly").FillAsync(bankAccount);
        await _page.Locator(".modal-actions").GetByText("Add staff", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.GetByText("Staff profile created")).ToBeVisibleAsync();
        await _page.GetByText("Done").ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        var displayName = name.ToUpperInvariant();
        await _page.GetByText(displayName).ClickAsync();
        await Assertions.Expect(_page.Locator(".drawer")).ToBeVisibleAsync();

        return displayName;
    }

    [Fact]
    public async Task Drawer_Opens_ShowingTheCorrectStaffMembersIdentity()
    {
        await AuthHelper.SignInAsync(_page);
        var displayName = await CreateStaffMemberAndOpenDrawerAsync();

        // .drawer-name specifically, not just "the text appears somewhere
        // on the page" - the directory row behind the drawer also shows
        // this same name, so this confirms the DRAWER itself displays it,
        // not just that it exists elsewhere on screen.
        await Assertions.Expect(_page.Locator(".drawer-name")).ToHaveTextAsync(displayName);
    }

    [Fact]
    public async Task Drawer_SwitchingTabs_ShowsTheCorrectTabsOwnContent()
    {
        await AuthHelper.SignInAsync(_page);
        await CreateStaffMemberAndOpenDrawerAsync();

        // Personal tab's content visible by default.
        await Assertions.Expect(_page.Locator(".drawer").GetByText("Display name")).ToBeVisibleAsync();

        await _page.Locator(".drawer-tab").Filter(new LocatorFilterOptions { HasText = "Banking" }).ClickAsync();

        // Banking-specific content now visible, Personal-specific content gone.
        await Assertions.Expect(_page.Locator(".drawer").GetByText("Bank IFSC code")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator(".drawer").GetByText("Display name")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task Drawer_EditingDisplayName_PersistsAfterSaving()
    {
        await AuthHelper.SignInAsync(_page);
        await CreateStaffMemberAndOpenDrawerAsync();

        await _page.Locator(".drawer").GetByTitle("Edit").ClickAsync();

        var newName = $"E2E RENAMED {(uint)Guid.NewGuid().GetHashCode()}";
        var displayNameField = _page.Locator("div.form-row:has(label:text('Display name')) input");
        await displayNameField.FillAsync(newName);

        await _page.Locator(".drawer").GetByText("Save changes").ClickAsync();

        // The header's own name display updates - not just the input's
        // value, confirming the save genuinely went through and the UI
        // reflects the new, real state rather than just what was typed.
        await Assertions.Expect(_page.Locator(".drawer-name")).ToHaveTextAsync(newName);
    }

    [Fact]
    public async Task Drawer_CloseButton_ClosesTheDrawer()
    {
        await AuthHelper.SignInAsync(_page);
        await CreateStaffMemberAndOpenDrawerAsync();

        await _page.Locator(".drawer").GetByTitle("Close").ClickAsync();

        await Assertions.Expect(_page.Locator(".drawer")).ToBeHiddenAsync();
    }
}
