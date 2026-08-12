using Microsoft.Playwright;
using ShanthiNikethan.EmployeeManagement.E2ETests.TestHelpers;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.E2ETests;

public class LeaveTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    public LeaveTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _page = await _fixture.NewIsolatedPageAsync();

    public async Task DisposeAsync() => await _page.Context.CloseAsync();

    /// <summary>
    /// Creates a real, active staff member via the already-proven Add
    /// Staff flow - every test here needs one to record leave against.
    /// </summary>
    private async Task<string> CreateStaffMemberAsync(string? explicitName = null)
    {
        var uniqueNum = (uint)Guid.NewGuid().GetHashCode();
        var name = explicitName ?? $"E2E Leave Test {uniqueNum}";
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

    /// <summary>
    /// The staff dropdown's option values are each person's real GUID -
    /// not something a test can predict. This finds the right option by
    /// its visible name text (which the test DOES control), reads its
    /// actual value attribute, then selects using that - safer than
    /// guessing at the exact "Name (Code)" label string formatting.
    /// </summary>
    private async Task SelectStaffInDropdownAsync(string displayName)
    {
        var option = _page.Locator(".modal select option").Filter(new LocatorFilterOptions { HasText = displayName });
        var value = await option.GetAttributeAsync("value");
        await _page.Locator(".modal select").SelectOptionAsync(value!);
    }

    [Fact]
    public async Task AddLeave_HappyPath_CreatesARecordVisibleInTheWeekView()
    {
        await AuthHelper.SignInAsync(_page);
        var displayName = await CreateStaffMemberAsync();

        await _page.GotoAsync($"{E2EConfig.BaseUrl}/leave");
        await _page.GetByText("Add leave record", new PageGetByTextOptions { Exact = false }).ClickAsync();

        await SelectStaffInDropdownAsync(displayName);
        // Start/end date default to today (confirmed in OpenAddDialog) -
        // a single day's leave needs no date changes at all for this
        // happy-path test.
        await _page.Locator("div.form-row:has(label:text('Reason (optional)')) input").FillAsync("E2E test leave");

        await _page.Locator(".modal-actions").GetByText("Save", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        // The Week view (default) shows today's leave as a chip - this
        // confirms the whole round trip actually happened, not just that
        // the dialog closed without an error.
        await Assertions.Expect(_page.GetByText(displayName)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddLeave_ChangingStartDate_ResetsEndDateToMatch()
    {
        // Confirms a specific, deliberate design choice (explained
        // directly in OnStartDateChanged's own comment): changing the
        // start date always resets the end date to match it, defaulting
        // to a single-day leave as the common case - never left combining
        // a new start date with a stale, unrelated end date.
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/leave");
        await _page.GetByText("Add leave record", new PageGetByTextOptions { Exact = false }).ClickAsync();

        var futureDate = DateTime.Today.AddDays(10).ToString("yyyy-MM-dd");
        await _page.Locator("div.form-row:has(label:text('Start date *')) input").FillAsync(futureDate);

        var endDateField = _page.Locator("div.form-row:has(label:text('End date *')) input");
        await Assertions.Expect(endDateField).ToHaveValueAsync(futureDate);

        await _page.Locator(".modal-actions").GetByText("Cancel").ClickAsync();
    }

    [Fact]
    public async Task EditLeave_ClickingAChip_OpensPrefilled_AndCanBeDeleted()
    {
        await AuthHelper.SignInAsync(_page);
        var displayName = await CreateStaffMemberAsync();

        await _page.GotoAsync($"{E2EConfig.BaseUrl}/leave");
        await _page.GetByText("Add leave record", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await SelectStaffInDropdownAsync(displayName);
        await _page.Locator(".modal-actions").GetByText("Save", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        // Clicking the chip itself opens the edit dialog (confirmed in
        // OpenEditDialog) - the heading shows the person's real name
        // directly, not a re-selectable dropdown, since who the record
        // belongs to isn't something edit mode changes.
        await _page.GetByText(displayName).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal h2")).ToContainTextAsync(displayName);

        await _page.Locator(".modal-actions").GetByText("Delete", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.GetByText("Delete this leave record?")).ToBeVisibleAsync();

        await _page.Locator(".modal-actions").GetByText("Delete", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        // The chip is genuinely gone, not just the dialog closed.
        await Assertions.Expect(_page.GetByText(displayName)).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddLeave_MultiDayDateRange_AutoCalculatesTheCorrectDaysCount()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/leave");
        await _page.GetByText("Add leave record", new PageGetByTextOptions { Exact = false }).ClickAsync();

        var start = DateTime.Today.AddDays(5);
        await _page.Locator("div.form-row:has(label:text('Start date *')) input").FillAsync(start.ToString("yyyy-MM-dd"));
        // 4 calendar days apart - start, +1, +2, end - should compute to 4 days total.
        await _page.Locator("div.form-row:has(label:text('End date *')) input").FillAsync(start.AddDays(3).ToString("yyyy-MM-dd"));

        var daysField = _page.Locator("div.form-row:has(label:text('Number of days *')) input");
        await Assertions.Expect(daysField).ToHaveValueAsync("4");

        await _page.Locator(".modal-actions").GetByText("Cancel").ClickAsync();
    }

    [Fact]
    public async Task AddLeave_TypingDaysCount_MovesEndDateToMatch()
    {
        // The reverse direction of the multi-day test above - confirmed
        // directly in OnDaysCountChanged's own comment: typing a Days
        // value moves the End Date to match, rather than only dates ever
        // driving the day count.
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/leave");
        await _page.GetByText("Add leave record", new PageGetByTextOptions { Exact = false }).ClickAsync();

        var daysField = _page.Locator("div.form-row:has(label:text('Number of days *')) input");
        await daysField.FillAsync("3");
        await daysField.PressAsync("Tab"); // @bind:after fires on blur, not every keystroke

        var expectedEndDate = DateTime.Today.AddDays(2).ToString("yyyy-MM-dd"); // 3 days total = start + 2
        var endDateField = _page.Locator("div.form-row:has(label:text('End date *')) input");
        await Assertions.Expect(endDateField).ToHaveValueAsync(expectedEndDate);

        await _page.Locator(".modal-actions").GetByText("Cancel").ClickAsync();
    }

    [Fact]
    public async Task AddLeave_WithoutSelectingStaff_ShowsValidationError_AndDoesNotSave()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/leave");
        await _page.GetByText("Add leave record", new PageGetByTextOptions { Exact = false }).ClickAsync();

        // Deliberately leaving "— Select —" as-is, never choosing a staff member.
        await _page.Locator(".modal-actions").GetByText("Save", new LocatorGetByTextOptions { Exact = true }).ClickAsync();

        await Assertions.Expect(_page.GetByText("Select a staff member.")).ToBeVisibleAsync();
        // Still open - a validation failure never got as far as closing the dialog.
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ParseWhatsAppMessage_FillsTheFormFieldsCorrectly()
    {
        // Genuinely testable despite the name - WhatsAppLeaveParser is
        // pure text parsing (confirmed directly in its source), with no
        // actual WhatsApp integration involved at all. A constructed
        // sample message exercises the real parsing logic exactly the
        // same way a pasted real one would.
        //
        // Uses a name that does NOT start with "E2E" deliberately - the
        // parser's matching logic scores based on each name's first
        // multi-character token, and every other test-created staff
        // member shares "E2E" as that first token, which would make this
        // match ambiguous against dozens of unrelated people instead of
        // resolving cleanly to this one.
        await AuthHelper.SignInAsync(_page);
        var uniqueNum = (uint)Guid.NewGuid().GetHashCode();
        var uniqueName = $"WAParseTarget{uniqueNum}";
        await CreateStaffMemberAsync(uniqueName);

        await _page.GotoAsync($"{E2EConfig.BaseUrl}/leave");
        await _page.GetByText("Add leave record", new PageGetByTextOptions { Exact = false }).ClickAsync();

        var sampleMessage = $"Name: {uniqueName}\nDate: 20/08/2026\nNo of days: 1\nReason: Family event";
        await _page.GetByPlaceholder("Paste the leave notification message here, then click Parse — it'll fill in the fields below for you to check.").FillAsync(sampleMessage);
        await _page.GetByText("Parse & fill form").ClickAsync();

        // Confirms the parser matched this one specific person, not just
        // that parsing ran without error.
        await Assertions.Expect(_page.GetByText($"Matched \"{uniqueName}\"")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("div.form-row:has(label:text('Reason (optional)')) input")).ToHaveValueAsync("Family event");
        await Assertions.Expect(_page.Locator("div.form-row:has(label:text('Start date *')) input")).ToHaveValueAsync("2026-08-20");

        await _page.Locator(".modal-actions").GetByText("Cancel").ClickAsync();
    }
}
