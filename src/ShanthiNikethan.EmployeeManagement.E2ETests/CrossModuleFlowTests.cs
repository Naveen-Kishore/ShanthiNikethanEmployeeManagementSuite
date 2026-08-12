using Microsoft.Playwright;
using ShanthiNikethan.EmployeeManagement.E2ETests.TestHelpers;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.E2ETests;

/// <summary>
/// Every other test file in this suite proves one module works in
/// isolation. This file is deliberately different - it follows a single
/// staff member or action ACROSS modules, the way a real user's day
/// actually would, rather than starting fresh in each module's own
/// bubble. Each test spans exactly two modules with one cohesive story,
/// not everything at once - keeps failures debuggable.
/// </summary>
public class CrossModuleFlowTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    public CrossModuleFlowTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _page = await _fixture.NewIsolatedPageAsync();

    public async Task DisposeAsync() => await _page.Context.CloseAsync();

    private async Task<string> CreateStaffMemberAsync()
    {
        var uniqueNum = (uint)Guid.NewGuid().GetHashCode();
        var name = $"E2E CrossFlow {uniqueNum}";
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

    [Fact]
    public async Task RemovingStaff_HidesThemFromAttendanceAndNewLeaveEntries()
    {
        await AuthHelper.SignInAsync(_page);
        var name = await CreateStaffMemberAsync();

        // Confirmed present in both modules BEFORE removal - the
        // baseline this test's real point rests on. Nav-link clicks from
        // here on, not repeated GotoAsync - three-plus full-page
        // reloads within one test turned out to be a genuinely
        // different, less reliable stress on the Blazor circuit than
        // the single GotoAsync-per-test pattern proven safe everywhere
        // else in this suite.
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Attendance" }).ClickAsync();
        await Assertions.Expect(_page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name })).ToBeVisibleAsync();

        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Leave Management" }).ClickAsync();
        await _page.GetByText("Add leave record", new PageGetByTextOptions { Exact = false }).ClickAsync();
        // Count, not visibility - <option> elements inside a native
        // <select> aren't rendered as normally "visible" DOM elements
        // the way regular tags are, so ToBeVisibleAsync is the wrong
        // check here regardless of whether the option genuinely exists.
        await Assertions.Expect(_page.Locator(".modal select option").Filter(new LocatorFilterOptions { HasText = name })).ToHaveCountAsync(1);
        await _page.Locator(".modal-actions").GetByText("Cancel").ClickAsync();

        // Now remove them via Staff Directory.
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Staff Directory" }).ClickAsync();
        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name }).Locator("input[type='checkbox']").CheckAsync();
        await _page.GetByText("Delete staff", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await _page.Locator(".modal-actions").GetByText("Remove", new LocatorGetByTextOptions { Exact = false }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        // Now confirm they're genuinely gone from both - not just from
        // Staff Directory's own Active list.
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Attendance" }).ClickAsync();
        await Assertions.Expect(_page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name })).Not.ToBeVisibleAsync();

        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Leave Management" }).ClickAsync();
        await _page.GetByText("Add leave record", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal select option").Filter(new LocatorFilterOptions { HasText = name })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task EditingDisplayName_PropagatesToAttendanceMarking()
    {
        await AuthHelper.SignInAsync(_page);
        var originalName = await CreateStaffMemberAsync();

        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Attendance" }).ClickAsync();
        await Assertions.Expect(_page.Locator("tr").Filter(new LocatorFilterOptions { HasText = originalName })).ToBeVisibleAsync();

        // Edit the name via the Staff Directory drawer, not Attendance -
        // the whole point is confirming the change reaches a DIFFERENT
        // module's own display, not just StaffProfile's.
        var newName = $"E2E RENAMED CROSSFLOW {(uint)Guid.NewGuid().GetHashCode()}";
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Staff Directory" }).ClickAsync();
        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = originalName }).ClickAsync();
        // The drawer opens read-only by default - every field, including
        // Display name, stays readonly until this pencil icon is
        // clicked, confirmed both directly by the user and by the
        // pattern already proven correct in StaffProfileDrawerTests.cs.
        await _page.Locator(".drawer").GetByTitle("Edit").ClickAsync();
        // "Display name", not "Full name *" - that label only exists in
        // the Add Staff dialog (used correctly above, inside
        // CreateStaffMemberAsync). The drawer for an EXISTING staff
        // member is a different component with its own field labels,
        // confirmed directly by a screenshot of the actual failure and
        // matching the pattern already proven correct in
        // StaffProfileDrawerTests.cs.
        await _page.Locator("div.form-row:has(label:text('Display name')) input").FillAsync(newName);
        await _page.Keyboard.PressAsync("Tab");
        await _page.Locator(".drawer-footer").GetByText("Save").ClickAsync();
        // The drawer stays open after saving, showing updated state -
        // the same pattern already seen in Access Management's
        // Deactivate/Reactivate. Confirming the save took effect within
        // the still-open drawer first, then closing it explicitly.
        await Assertions.Expect(_page.Locator(".drawer").GetByText(newName.ToUpperInvariant())).ToBeVisibleAsync();
        await _page.Locator(".drawer").GetByTitle("Close").ClickAsync();
        await Assertions.Expect(_page.Locator(".drawer")).ToBeHiddenAsync();

        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Attendance" }).ClickAsync();
        await Assertions.Expect(_page.Locator("tr").Filter(new LocatorFilterOptions { HasText = newName.ToUpperInvariant() })).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("tr").Filter(new LocatorFilterOptions { HasText = originalName })).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// Same tick-based approach already proven in PayrollTests.cs - the
    /// (Year, Month, RunType) uniqueness constraint is real and
    /// enforced server-side, confirmed directly in the app's own code.
    /// Leaving Month/Year at their defaults here would risk exactly the
    /// collision that took several rounds to properly solve there.
    /// </summary>
    private (int Month, int Year) UniqueMonthYear()
    {
        var offset = (int)(DateTime.UtcNow.Ticks % 48);
        var month = (offset % 12) + 1;
        var year = DateTime.Today.Year - 2 + (offset / 12);
        return (month, year);
    }

    [Fact]
    public async Task CreatingAPayrollRun_ProducesARealAuditLogEntry()
    {
        // AuditLogTests only ever confirmed VIEWING existing data - never
        // that a fresh, specific action taken in a completely different
        // module reliably produces a real, corresponding entry. A
        // uniquely-labeled Other-type run makes this directly searchable
        // rather than guessing at what audit text to expect.
        await AuthHelper.SignInAsync(_page);
        var uniqueLabel = $"E2E CrossFlow Audit {(uint)Guid.NewGuid().GetHashCode()}";
        var (month, year) = UniqueMonthYear();

        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Payroll" }).ClickAsync();
        await _page.GetByText("Create payroll run", new PageGetByTextOptions { Exact = false }).ClickAsync();
        var selects = _page.Locator(".modal select");
        await selects.Nth(0).SelectOptionAsync(month.ToString());
        await selects.Nth(1).SelectOptionAsync(year.ToString());
        await selects.Nth(2).SelectOptionAsync("Other");
        await _page.GetByPlaceholder("e.g. Diwali Bonus").FillAsync(uniqueLabel);
        await _page.Keyboard.PressAsync("Tab");
        await _page.Locator(".modal-actions").GetByText("Create", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/payroll/[0-9a-fA-F-]{36}$"));

        await _page.GetByText("Administration", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await _page.GetByText("Audit Log", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await _page.GetByPlaceholder("Search details…").FillAsync(uniqueLabel);
        await _page.GetByText("Apply filters", new PageGetByTextOptions { Exact = false }).ClickAsync();

        await Assertions.Expect(_page.Locator("table.grid tbody tr").First).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("No activity matches these filters.")).Not.ToBeVisibleAsync();

        // Cleanup - discards the Draft run this test created, same
        // discipline as PayrollTests.cs, avoiding permanent accumulation.
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Payroll" }).ClickAsync();
        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = uniqueLabel }).ClickAsync();
        await _page.GetByText("Discard draft").ClickAsync();
        await _page.Locator(".modal-actions").GetByText("Discard").ClickAsync();
    }

    [Fact]
    public async Task ReactivatingStaff_RestoresThemToAttendanceAndLeave()
    {
        // The natural complement to RemovingStaff_HidesThemFrom... above -
        // confirms the restore direction works too, not just the hide
        // direction. Same lifecycle-symmetry reasoning already applied
        // to Remove/Reactivate in RemoveStaffTests.cs.
        await AuthHelper.SignInAsync(_page);
        var name = await CreateStaffMemberAsync();

        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Staff Directory" }).ClickAsync();
        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name }).Locator("input[type='checkbox']").CheckAsync();
        await _page.GetByText("Delete staff", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await _page.Locator(".modal-actions").GetByText("Remove", new LocatorGetByTextOptions { Exact = false }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        await _page.GetByText("Soft-deleted", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name })
            .GetByText("Reactivate").ClickAsync();

        // Confirmed restored in Attendance and Leave's dropdown - the
        // same two modules the removal side of this story checked.
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Attendance" }).ClickAsync();
        await Assertions.Expect(_page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name })).ToBeVisibleAsync();

        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Leave Management" }).ClickAsync();
        await _page.GetByText("Add leave record", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal select option").Filter(new LocatorFilterOptions { HasText = name })).ToHaveCountAsync(1);
    }
}
