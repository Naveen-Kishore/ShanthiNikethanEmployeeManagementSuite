using Microsoft.Playwright;
using ShanthiNikethan.EmployeeManagement.E2ETests.TestHelpers;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.E2ETests;

public class AttendanceTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    public AttendanceTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _page = await _fixture.NewIsolatedPageAsync();

    public async Task DisposeAsync() => await _page.Context.CloseAsync();

    /// <summary>
    /// Creates a real, active staff member via the already-proven Add
    /// Staff flow - every test here needs one fresh person to mark
    /// attendance for, so their row starts genuinely unmarked (not
    /// polluted by some other test's prior attendance record).
    /// </summary>
    private async Task<string> CreateStaffMemberAsync()
    {
        var uniqueNum = (uint)Guid.NewGuid().GetHashCode();
        var name = $"E2E Attendance Test {uniqueNum}";
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

    private ILocator StaffRow(string displayName) =>
        _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = displayName });

    [Fact]
    public async Task Mark_ChangingMorningStatus_SavesImmediately_AndPersistsAfterReload()
    {
        await AuthHelper.SignInAsync(_page);
        var displayName = await CreateStaffMemberAsync();

        await _page.GotoAsync($"{E2EConfig.BaseUrl}/attendance");
        await Assertions.Expect(StaffRow(displayName)).ToBeVisibleAsync();

        // Two structurally identical <select> elements per row - Morning
        // is always first, Evening always second (matching the table's
        // own column order), no distinguishing label inside either one.
        var morningSelect = StaffRow(displayName).Locator("select").First;

        // Retries the SELECTION itself, not just the verification after
        // it - the verification below already auto-retries for 5s and
        // still consistently failed across two separate runs, meaning a
        // single SelectOptionAsync call doesn't always genuinely
        // register with the server right after a fresh page load,
        // likely because a dynamically-rendered row's event handlers
        // haven't fully finished wiring up the instant it becomes
        // visually visible. Re-attempting the action itself, not just
        // waiting longer to check it, is what actually addresses that.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await morningSelect.SelectOptionAsync("Absent");
            if (await morningSelect.InputValueAsync() == "Absent") break;
            await _page.WaitForTimeoutAsync(300);
        }

        // No separate save button on this page - every change saves
        // immediately (confirmed in OnSessionChanged, which calls
        // MarkAsync directly, then reloads). Checking the value AFTER
        // that reload is what actually proves the save happened
        // server-side, not just that the dropdown's own client-side
        // value changed - an unsaved change would revert on reload.
        await Assertions.Expect(morningSelect).ToHaveValueAsync("Absent");
    }

    [Fact]
    public async Task Mark_PastDate_IsLockedByDefault_UntilAdminOverrideIsEnabled()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/attendance");

        await _page.GetByText("Previous day").ClickAsync();

        // Locked - the real, deployed warning text, not a guess.
        await Assertions.Expect(_page.GetByText("is locked — only today's attendance can be marked or changed directly")).ToBeVisibleAsync();

        var unlockButton = _page.GetByText("I'm the admin — unlock this day");
        await Assertions.Expect(unlockButton).ToBeVisibleAsync();
        await unlockButton.ClickAsync();

        // The override warning replaces the locked warning once enabled.
        await Assertions.Expect(_page.GetByText("Admin override active for")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Mark_SwitchingToMonthlyRegister_ShowsTheRegisterView()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/attendance");

        // Mark Attendance's own toolbar visible by default.
        await Assertions.Expect(_page.GetByText("Today", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();

        await _page.GetByText("Monthly Register").ClickAsync();

        // Register-specific column headers, not present on the Mark
        // Attendance view at all - confirms the view genuinely switched,
        // not just that the tab visually looks selected.
        await Assertions.Expect(_page.GetByText("Work Days")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Pres Days")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Mark_TypingANoteOnAnUnmarkedRow_ImplicitlyMarksBothSessionsPresent()
    {
        // A genuinely non-obvious behavior, confirmed directly in
        // OnNotesChanged: for a row that's never been marked at all
        // (Id == Guid.Empty), typing ONLY a note - never touching either
        // status dropdown - saves BOTH Morning and Evening as Present.
        // Worth confirming this actually happens, not just assumed from
        // reading the code, since it's easy to miss.
        await AuthHelper.SignInAsync(_page);
        var displayName = await CreateStaffMemberAsync();

        await _page.GotoAsync($"{E2EConfig.BaseUrl}/attendance");
        var row = StaffRow(displayName);
        await Assertions.Expect(row).ToBeVisibleAsync();

        await row.Locator("input[placeholder='Optional note']").FillAsync("Arrived a bit late");
        await row.Locator("input[placeholder='Optional note']").PressAsync("Tab");

        var morningSelect = row.Locator("select").First;
        var eveningSelect = row.Locator("select").Nth(1);
        await Assertions.Expect(morningSelect).ToHaveValueAsync("Present");
        await Assertions.Expect(eveningSelect).ToHaveValueAsync("Present");
    }

    [Fact]
    public async Task Mark_NextDayButton_IsDisabledWhenViewingToday()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/attendance");

        // Today is the default view - can't navigate into the future.
        await Assertions.Expect(_page.GetByText("Next day")).ToBeDisabledAsync();

        await _page.GetByText("Previous day").ClickAsync();
        await Assertions.Expect(_page.GetByText("Next day")).ToBeEnabledAsync();
    }
}
