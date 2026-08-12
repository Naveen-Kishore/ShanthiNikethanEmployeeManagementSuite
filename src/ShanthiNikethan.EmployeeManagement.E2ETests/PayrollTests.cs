using Microsoft.Playwright;
using ShanthiNikethan.EmployeeManagement.E2ETests.TestHelpers;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.E2ETests;

public class PayrollTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    // Guards the one-time sweep below so it only actually runs once per
    // process, not once before every single test in this class.
    private static bool _staleRunsSwept = false;

    public PayrollTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        _page = await _fixture.NewIsolatedPageAsync();

        if (!_staleRunsSwept)
        {
            _staleRunsSwept = true;
            await SweepStaleE2ERunsAsync();
        }
    }

    public async Task DisposeAsync()
    {
        // Safety net for tests that failed before reaching their own
        // end-of-test DiscardCurrentRunAsync() call. The sweep in
        // InitializeAsync only runs once, at the very start of a whole
        // dotnet test invocation - it does nothing about a run left
        // behind mid-run by a test that failed partway through, which
        // could still collide with a later test in this same execution.
        // Checking whether "Discard draft" happens to be visible on
        // whatever page a failure left the test on handles every case
        // correctly without needing to track per-test state: a
        // Published run never shows that button at all, so PublishRun's
        // deliberately-permanent result is naturally left untouched.
        try
        {
            var discardButton = _page.GetByText("Discard draft");
            if (await discardButton.IsVisibleAsync())
            {
                await discardButton.ClickAsync();
                await _page.Locator(".modal-actions").GetByText("Discard").ClickAsync();
            }
        }
        catch
        {
            // Best-effort only - the page may already be closed, mid-
            // navigation, or in some other unexpected state after a
            // failure. This must never mask the test's real failure.
        }

        await _page.Context.CloseAsync();
    }

    /// <summary>
    /// Clears out old, E2E-created Draft runs left behind by PRIOR
    /// dotnet test executions - the per-test cleanup added elsewhere in
    /// this file only stops future accumulation within one run; it does
    /// nothing about debris that already existed before this run even
    /// started. That debris is exactly what caused today's remaining
    /// collisions - both were against runs with different hash numbers
    /// than what the current attempt was creating, confirming they
    /// predated this run entirely. Published runs are deliberately left
    /// alone - they can't be discarded (locking permanently is the
    /// whole point), so they're an accepted, unavoidable, slowly-growing
    /// cost rather than something this sweep can or should touch.
    /// </summary>
    private async Task SweepStaleE2ERunsAsync()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Payroll" }).ClickAsync();

        var staleRow = _page.Locator("tr").Filter(new LocatorFilterOptions { HasTextString = "E2E " })
            .Filter(new LocatorFilterOptions { HasTextString = "Draft" });

        // Capped iteration count as a safety net against any unexpected
        // infinite loop, rather than looping with no upper bound at all.
        for (var i = 0; i < 100 && await staleRow.CountAsync() > 0; i++)
        {
            await staleRow.First.ClickAsync();
            await DiscardCurrentRunAsync();
        }
    }

    /// <summary>
    /// CreateDraftAsync enforces a real uniqueness constraint on
    /// (Year, Month, RunType) - confirmed directly in PayrollService.cs -
    /// and critically, the custom label is NOT part of that check. That
    /// means two "Other" runs for the same month collide regardless of
    /// their labels, the same class of risk the staff-code race
    /// condition turned out to be earlier. Every test that creates a run
    /// picks its own random month/year, keeping tests independent of
    /// each other and safe across repeated suite runs.
    /// </summary>
    private (int Month, int Year) UniqueMonthYear()
    {
        // Tick-based rather than purely random - with only 48 valid
        // (month, year) slots total (12 months x 4 years), 6 independent
        // random picks across this file's tests have a real, non-negligible
        // chance of colliding with each other, not just across separate
        // suite runs. Ticks are 100-nanosecond resolution and effectively
        // never repeat within one process, spreading calls reliably
        // across the full slot range instead of trusting randomness alone.
        var offset = (int)(DateTime.UtcNow.Ticks % 48);
        var month = (offset % 12) + 1;
        var year = DateTime.Today.Year - 2 + (offset / 12);
        return (month, year);
    }

    /// <summary>
    /// DOM order inside the create dialog is: month select, year select,
    /// THEN run type select - three <select> elements total, not one.
    /// An earlier version of this file assumed the type select was
    /// .First, which was actually the month picker's own first dropdown -
    /// a real bug, not a hypothetical one.
    /// </summary>
    private async Task OpenCreateDialogAndFillAsync(int month, int year, string runType)
    {
        await _page.GetByText("Create payroll run", new PageGetByTextOptions { Exact = false }).ClickAsync();
        var selects = _page.Locator(".modal select");
        await selects.Nth(0).SelectOptionAsync(month.ToString());
        await selects.Nth(1).SelectOptionAsync(year.ToString());
        await selects.Nth(2).SelectOptionAsync(runType);
    }

    /// <summary>
    /// Cleans up a Draft run left behind by a test, run from that run's
    /// own detail page. This is the real fix for month/year collisions
    /// between separate suite runs over time - the 48-slot space
    /// (12 months x 4 allowed years) was never actually saturated within
    /// one run, but repeated runs across this whole session left every
    /// created Draft permanently in the database with nothing ever
    /// cleaning them up, until the slot space genuinely ran out.
    /// Published runs are the one deliberate exception - locking
    /// permanently is the entire point of that feature, so that one
    /// slot per suite run is an accepted, unavoidable cost.
    /// </summary>
    private async Task DiscardCurrentRunAsync()
    {
        await _page.GetByText("Discard draft").ClickAsync();
        await _page.Locator(".modal-actions").GetByText("Discard").ClickAsync();
        await Assertions.Expect(_page).ToHaveURLAsync($"{E2EConfig.BaseUrl}/payroll");
    }

    [Fact]
    public async Task CreateRun_DuplicateMonthAndType_IsRejected()
    {
        await AuthHelper.SignInAsync(_page);
        var (month, year) = UniqueMonthYear();

        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Payroll" }).ClickAsync();
        await OpenCreateDialogAndFillAsync(month, year, "Other");
        var firstLabel = $"E2E Duplicate Check {(uint)Guid.NewGuid().GetHashCode()}";
        await _page.GetByPlaceholder("e.g. Diwali Bonus").FillAsync(firstLabel);
        await _page.Locator(".modal-actions").GetByText("Create", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/payroll/[0-9a-fA-F-]{36}$"));

        // Back to the list, then a SECOND "Other" run for the exact same
        // month/year - a genuinely different label doesn't matter, since
        // the label was never part of the uniqueness check at all.
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Payroll" }).ClickAsync();
        await OpenCreateDialogAndFillAsync(month, year, "Other");
        await _page.GetByPlaceholder("e.g. Diwali Bonus").FillAsync($"E2E Different Label {(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Locator(".modal-actions").GetByText("Create", new LocatorGetByTextOptions { Exact = true }).ClickAsync();

        await Assertions.Expect(_page.GetByText("already exists")).ToBeVisibleAsync();
        // Still on the list - the rejected create never navigated anywhere.
        await Assertions.Expect(_page).ToHaveURLAsync($"{E2EConfig.BaseUrl}/payroll");

        // Only clean up the first, successfully-created run now, after
        // the duplicate rejection has already been proven - discarding
        // it any earlier would mean the second attempt was never
        // actually colliding with anything. The rejected create leaves
        // its dialog open with the error message inside it - close that
        // via its own Cancel button before the list row is clickable.
        await _page.Locator(".modal-actions").GetByText("Cancel").ClickAsync();
        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = firstLabel }).ClickAsync();
        await DiscardCurrentRunAsync();
    }

    [Fact]
    public async Task EditLineItemAmount_UpdatesTheRunningGrandTotal()
    {
        // "Other" type specifically - RegularSalary line items are
        // never editable at all (confirmed: the amount input only
        // renders when Draft && !IsRegularSalary), and every active
        // staff member starts in an "Other" run's draft at zero.
        await AuthHelper.SignInAsync(_page);
        var (month, year) = UniqueMonthYear();
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Payroll" }).ClickAsync();
        await OpenCreateDialogAndFillAsync(month, year, "Other");
        await _page.GetByPlaceholder("e.g. Diwali Bonus").FillAsync($"E2E Edit Amount {(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Locator(".modal-actions").GetByText("Create", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/payroll/[0-9a-fA-F-]{36}$"));

        await _page.GetByTitle("Show amounts").ClickAsync();

        var firstAmountInput = _page.Locator("table.grid tbody tr").First.Locator("input[type='number']");
        await firstAmountInput.FillAsync("5000");
        await firstAmountInput.PressAsync("Tab");

        // Scoped specifically to the calc-panel's own total row (its own
        // dedicated CSS class, confirmed in the real markup) rather than
        // a bare text match - with only one line item edited and every
        // other still at zero, the total legitimately equals the same
        // amount as that one row, so an unscoped match would be
        // genuinely ambiguous between two both-correct matches.
        await Assertions.Expect(_page.Locator(".calc-row.total .val")).ToHaveTextAsync("₹5,000");

        await DiscardCurrentRunAsync();
    }

    [Fact]
    public async Task RemoveLineItem_SelectedRow_DisappearsFromTheDraft()
    {
        await AuthHelper.SignInAsync(_page);
        var (month, year) = UniqueMonthYear();
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Payroll" }).ClickAsync();
        await OpenCreateDialogAndFillAsync(month, year, "Other");
        await _page.GetByPlaceholder("e.g. Diwali Bonus").FillAsync($"E2E Remove Item {(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Locator(".modal-actions").GetByText("Create", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/payroll/[0-9a-fA-F-]{36}$"));

        var firstRow = _page.Locator("table.grid tbody tr").First;
        var firstRowName = await firstRow.Locator("td").Nth(1).InnerTextAsync();
        await firstRow.Locator("input[type='checkbox']").CheckAsync();

        await _page.GetByText("Remove from this run", new PageGetByTextOptions { Exact = false }).ClickAsync();

        // The specific row that was checked is genuinely gone, not just
        // that the row count happened to change.
        await Assertions.Expect(_page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = firstRowName })).Not.ToBeVisibleAsync();

        await DiscardCurrentRunAsync();
    }

    [Fact]
    public async Task PublishRun_LocksIt_AndRemovesEditingControls()
    {
        await AuthHelper.SignInAsync(_page);
        var (month, year) = UniqueMonthYear();
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Payroll" }).ClickAsync();
        await OpenCreateDialogAndFillAsync(month, year, "Other");
        await _page.GetByPlaceholder("e.g. Diwali Bonus").FillAsync($"E2E Publish Test {(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Locator(".modal-actions").GetByText("Create", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/payroll/[0-9a-fA-F-]{36}$"));

        await _page.GetByText("Publish & lock", new PageGetByTextOptions { Exact = false }).First.ClickAsync();
        await Assertions.Expect(_page.GetByText("This locks every number in this run permanently")).ToBeVisibleAsync();
        await _page.Locator(".modal-actions").GetByText("Publish & lock", new LocatorGetByTextOptions { Exact = false }).ClickAsync();

        await Assertions.Expect(_page.GetByText("Published", new PageGetByTextOptions { Exact = true })).ToBeVisibleAsync();

        // Draft-only controls (Discard, checkboxes, editable amounts) are
        // conditionally rendered on Status == Draft - confirms the page
        // genuinely reflects the new, locked state, not just that a
        // badge changed while everything else stayed editable underneath.
        await Assertions.Expect(_page.GetByText("Discard draft")).Not.ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("table.grid input[type='checkbox']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task DiscardDraft_DeletesTheRun_AndReturnsToTheList()
    {
        await AuthHelper.SignInAsync(_page);
        var (month, year) = UniqueMonthYear();
        var label = $"E2E Discard Test {(uint)Guid.NewGuid().GetHashCode()}";
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Payroll" }).ClickAsync();
        await OpenCreateDialogAndFillAsync(month, year, "Other");
        await _page.GetByPlaceholder("e.g. Diwali Bonus").FillAsync(label);
        await _page.Locator(".modal-actions").GetByText("Create", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/payroll/[0-9a-fA-F-]{36}$"));

        await _page.GetByText("Discard draft").ClickAsync();
        await Assertions.Expect(_page.GetByText("This deletes the draft entirely")).ToBeVisibleAsync();
        await _page.Locator(".modal-actions").GetByText("Discard").ClickAsync();

        // Discard navigates back to the list on its own (confirmed in
        // DeleteAsync) - no separate "Back" click needed.
        await Assertions.Expect(_page).ToHaveURLAsync($"{E2EConfig.BaseUrl}/payroll");

        // Re-creating a run for the EXACT same month/type this test just
        // discarded proves the deletion was real and complete, not just
        // hidden from the list view - if the row were still in the
        // database, this second create would fail with the duplicate
        // error from CreateRun_DuplicateMonthAndType_IsRejected above.
        await OpenCreateDialogAndFillAsync(month, year, "Other");
        await _page.GetByPlaceholder("e.g. Diwali Bonus").FillAsync($"E2E Recreated {(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Locator(".modal-actions").GetByText("Create", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/payroll/[0-9a-fA-F-]{36}$"));

        // This second run only existed to prove the first one's deletion
        // was real - clean it up too, rather than leave it behind.
        await DiscardCurrentRunAsync();
    }

    [Fact]
    public async Task DownloadTeachingCsv_TriggersARealFileDownload()
    {
        // New Playwright territory for this project - capturing an
        // actual browser file download, not just an in-page UI change.
        // This confirms the download genuinely starts; it does not
        // inspect the file's contents, which would need a more complex
        // setup than this first pass attempts.
        await AuthHelper.SignInAsync(_page);
        var (month, year) = UniqueMonthYear();
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Payroll" }).ClickAsync();
        await OpenCreateDialogAndFillAsync(month, year, "Other");
        await _page.GetByPlaceholder("e.g. Diwali Bonus").FillAsync($"E2E CSV Download {(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Locator(".modal-actions").GetByText("Create", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/payroll/[0-9a-fA-F-]{36}$"));

        var download = await _page.RunAndWaitForDownloadAsync(async () =>
        {
            // Exact match - "Teaching CSV" is a literal substring of
            // "Non-Teaching CSV", so the default substring match was
            // genuinely ambiguous between the two buttons.
            await _page.GetByText("Teaching CSV", new PageGetByTextOptions { Exact = true }).ClickAsync();
        });

        Assert.EndsWith(".csv", download.SuggestedFilename);

        await DiscardCurrentRunAsync();
    }

    [Fact]
    public async Task CreateRun_OtherTypeWithoutALabel_ShowsValidationError()
    {
        // Real coverage from an earlier version of this file, lost when
        // it was fully rebuilt rather than merged - worth restoring
        // explicitly, not just assuming a rewrite preserved everything.
        await AuthHelper.SignInAsync(_page);
        var (month, year) = UniqueMonthYear();
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Payroll" }).ClickAsync();
        await OpenCreateDialogAndFillAsync(month, year, "Other");

        // Deliberately never filling the label field.
        await _page.Locator(".modal-actions").GetByText("Create", new LocatorGetByTextOptions { Exact = true }).ClickAsync();

        await Assertions.Expect(_page.GetByText("Enter a label for this payment type.")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task CreateRegularSalaryRun_LineItemAmountsAreNotEditable()
    {
        // Genuinely different behavior from every other test in this
        // file - all of which deliberately used "Other" for safety.
        // Regular Salary is the actual common real-world case, and its
        // amounts are a locked snapshot of real Net Pay (confirmed in
        // the create dialog's own text), never directly editable the
        // way an "Other" run's starting-at-zero amounts are.
        await AuthHelper.SignInAsync(_page);
        var (month, year) = UniqueMonthYear();
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Payroll" }).ClickAsync();
        await OpenCreateDialogAndFillAsync(month, year, "RegularSalary");
        await _page.Locator(".modal-actions").GetByText("Create", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"/payroll/[0-9a-fA-F-]{36}$"));

        // No number input anywhere in the table - amounts render as
        // plain (masked) text instead, confirmed directly in the
        // condition guarding that input: Draft && !IsRegularSalary.
        await Assertions.Expect(_page.Locator("table.grid input[type='number']")).ToHaveCountAsync(0);

        await DiscardCurrentRunAsync();
    }
}
