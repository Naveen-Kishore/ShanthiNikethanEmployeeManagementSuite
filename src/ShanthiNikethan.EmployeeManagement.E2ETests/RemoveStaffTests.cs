using Microsoft.Playwright;
using ShanthiNikethan.EmployeeManagement.E2ETests.TestHelpers;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.E2ETests;

public class RemoveStaffTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    public RemoveStaffTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _page = await _fixture.NewIsolatedPageAsync();

    public async Task DisposeAsync() => await _page.Context.CloseAsync();

    /// <summary>
    /// Creates a real staff member via the already-proven Add Staff flow.
    /// Every test here needs one to remove. Explicit, unique staff code -
    /// same fix as AddStaffTests/StaffProfileDrawerTests - auto-generation
    /// isn't safe under concurrent test classes.
    /// </summary>
    private async Task<string> CreateStaffMemberAsync()
    {
        var uniqueNum = (uint)Guid.NewGuid().GetHashCode();
        var name = $"E2E Remove Test {uniqueNum}";
        var tenDigits = uniqueNum.ToString("D10");
        var aadhaar = "99" + tenDigits;
        var phone = "9" + tenDigits.Substring(0, 9);
        var bankAccount = "E2E" + uniqueNum;

        await _page.GotoAsync($"{E2EConfig.BaseUrl}/staff");
        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await _page.Locator("div.form-row:has(label:text('Full name *')) input").FillAsync(name);
        await _page.GetByPlaceholder("Leave blank to auto-generate (e.g. SNM-T-048)").FillAsync("E2E-" + aadhaar.Substring(4));
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

        return name.ToUpperInvariant();
    }

    private ILocator RowCheckbox(string displayName) =>
        _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = displayName }).Locator("input[type='checkbox']");

    [Fact]
    public async Task Remove_DeleteStaffButton_StaysDisabled_UntilARowIsSelected()
    {
        await AuthHelper.SignInAsync(_page);
        var displayName = await CreateStaffMemberAsync();

        var deleteButton = _page.GetByText("Delete staff", new PageGetByTextOptions { Exact = false });
        await Assertions.Expect(deleteButton).ToBeDisabledAsync();

        await RowCheckbox(displayName).CheckAsync();

        await Assertions.Expect(deleteButton).ToBeEnabledAsync();
    }

    [Fact]
    public async Task Remove_Confirmed_MovesTheStaffMemberToSoftDeleted()
    {
        await AuthHelper.SignInAsync(_page);
        var displayName = await CreateStaffMemberAsync();

        await RowCheckbox(displayName).CheckAsync();
        await _page.GetByText("Delete staff", new PageGetByTextOptions { Exact = false }).ClickAsync();

        // The dialog names the specific person being removed, not a generic message.
        await Assertions.Expect(_page.GetByText($"Remove {displayName}")).ToBeVisibleAsync();

        await _page.Locator(".modal-actions").GetByText("Remove", new LocatorGetByTextOptions { Exact = false }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        // Gone from the default (Active) view...
        await Assertions.Expect(_page.GetByText(displayName)).Not.ToBeVisibleAsync();

        // ...but present under Soft-deleted, not actually destroyed - this
        // is the whole point of soft-delete over a hard delete, worth
        // confirming end-to-end rather than just trusting the label.
        await _page.GetByText("Soft-deleted", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await Assertions.Expect(_page.GetByText(displayName)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Remove_Cancel_LeavesTheStaffMemberActiveAndUnchanged()
    {
        await AuthHelper.SignInAsync(_page);
        var displayName = await CreateStaffMemberAsync();

        await RowCheckbox(displayName).CheckAsync();
        await _page.GetByText("Delete staff", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await Assertions.Expect(_page.GetByText($"Remove {displayName}")).ToBeVisibleAsync();

        await _page.Locator(".modal-actions").GetByText("Cancel").ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        // Still right there in Active, completely unaffected by opening
        // (and backing out of) the removal dialog.
        await Assertions.Expect(_page.GetByText(displayName)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Reactivate_MovesTheStaffMemberBackToActive()
    {
        // Completes the full lifecycle this file's been building toward:
        // create, remove (already covered above), and now the other
        // direction - the dialog explicitly promises reactivation is
        // possible within 60 days, worth confirming that's actually true
        // rather than just trusting the message.
        await AuthHelper.SignInAsync(_page);
        var displayName = await CreateStaffMemberAsync();

        await RowCheckbox(displayName).CheckAsync();
        await _page.GetByText("Delete staff", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await _page.Locator(".modal-actions").GetByText("Remove", new LocatorGetByTextOptions { Exact = false }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        await _page.GetByText("Soft-deleted", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await Assertions.Expect(_page.GetByText(displayName)).ToBeVisibleAsync();

        // Reactivate is a direct, one-click action (confirmed against
        // SoftDeletedList.razor directly) - no confirmation dialog to
        // navigate, unlike Remove.
        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = displayName })
            .GetByText("Reactivate").ClickAsync();

        // Gone from Soft-deleted...
        await Assertions.Expect(_page.GetByText(displayName)).Not.ToBeVisibleAsync();

        // ...and genuinely back in Active, not just removed from the
        // soft-deleted list without actually being restored anywhere.
        await _page.GetByText("Active", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await Assertions.Expect(_page.GetByText(displayName)).ToBeVisibleAsync();
    }
}
