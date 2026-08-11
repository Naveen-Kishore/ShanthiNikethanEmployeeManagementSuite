using Microsoft.Playwright;
using ShanthiNikethan.EmployeeManagement.E2ETests.TestHelpers;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.E2ETests;

public class AddStaffTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    public AddStaffTests(PlaywrightFixture fixture) => _fixture = fixture;

    // Fresh, isolated page (and its own browser context) before EVERY
    // test method - xUnit constructs a new instance of this class per
    // [Fact], so this genuinely runs once per test, not once per class.
    public async Task InitializeAsync() => _page = await _fixture.NewIsolatedPageAsync();

    // Explicitly closes the context after every test, pass or fail -
    // this is the fix for a real bug: an earlier version never closed
    // contexts at all, and across a growing number of tests that
    // accumulated into enough orphaned contexts to exhaust the shared
    // browser process mid-run.
    public async Task DisposeAsync() => await _page.Context.CloseAsync();

    /// <summary>
    /// These tests create real, persistent rows in the E2E database - not
    /// cleaned up automatically. Hardcoding the same Aadhaar/phone/bank
    /// account across repeated runs would collide with the app's own
    /// real, correct duplicate-detection on the second run - a false
    /// test failure, not an actual bug. Generating fresh, valid-format
    /// values each run avoids that entirely.
    /// </summary>
    private static (string Aadhaar, string Phone, string BankAccount, string Name) GenerateUniqueTestData(string namePrefix = "E2E Test Person")
    {
        var uniqueNum = (uint)Guid.NewGuid().GetHashCode(); // up to 10 digits, reliably unique per call
        var tenDigits = uniqueNum.ToString("D10");
        return (
            Aadhaar: "99" + tenDigits,                    // 2 + 10 = exactly 12 digits
            Phone: "9" + tenDigits.Substring(0, 9),        // 1 + 9 = exactly 10 digits, valid leading digit
            BankAccount: "E2E" + uniqueNum,
            Name: $"{namePrefix} {uniqueNum}"
        );
    }

    /// <summary>
    /// Fills exactly the fields CanSave actually requires (confirmed
    /// against AddStaffDialog.razor directly) - not every field on the
    /// form, since this is meant to prove the minimal happy path works,
    /// not exercise every optional field. Takes an explicit IPage
    /// parameter (rather than reading the class-level _page field
    /// directly) so it stays a plain, reusable helper independent of any
    /// one test class's lifecycle.
    /// </summary>
    private static async Task FillMinimalRequiredFieldsAsync(IPage page, string name, string aadhaar, string phone, string bankAccount)
    {
        await page.Locator("div.form-row:has(label:text('Full name *')) input").FillAsync(name);
        await page.Locator("div.form-row:has(label:text('Date of joining *')) input").FillAsync("2020-06-01");
        await page.GetByPlaceholder("10-digit mobile").FillAsync(phone);
        await page.GetByPlaceholder("12-digit number").FillAsync(aadhaar);
        await page.GetByPlaceholder("Type account number").FillAsync(bankAccount);
        await page.GetByPlaceholder("e.g. SBIN0001234").FillAsync("SBIN0001234");
        await page.GetByPlaceholder("Must match exactly").FillAsync(bankAccount);
    }

    [Fact]
    public async Task AddStaff_HappyPath_CreatesAVisibleStaffMember()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/staff");

        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();

        var (aadhaar, phone, bankAccount, name) = GenerateUniqueTestData();
        await FillMinimalRequiredFieldsAsync(_page, name, aadhaar, phone, bankAccount);

        await _page.Locator(".modal-actions").GetByText("Add staff", new LocatorGetByTextOptions { Exact = true }).ClickAsync();

        // The success screen, not just "no error appeared" - proves the
        // whole round trip (browser -> Blazor -> database -> back)
        // actually completed, not just that the click didn't crash.
        await Assertions.Expect(_page.GetByText("Staff profile created")).ToBeVisibleAsync();

        await _page.GetByText("Done").ClickAsync();

        // DisplayName is stored uppercase (confirmed in
        // ProceedWithCreationAsync: _name.Trim().ToUpperInvariant()) -
        // asserting against that real transformation, not the as-typed casing.
        await Assertions.Expect(_page.GetByText(name.ToUpperInvariant())).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddStaff_InvalidAadhaar_ShowsLiveErrorWithoutClickingSave()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/staff");
        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();

        // Deliberately too short - never clicking Add Staff at all in
        // this test, proving the LIVE validation itself works in a real
        // browser, not just the underlying C# logic already covered by
        // the 60 UserAccountServiceTests.
        await _page.GetByPlaceholder("12-digit number").FillAsync("12345");
        await _page.GetByPlaceholder("12-digit number").PressAsync("Tab");

        await Assertions.Expect(_page.GetByText("Must be exactly 12 digits.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddStaff_Button_StaysDisabled_UntilRequiredFieldsAreValid()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/staff");
        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();

        var saveButton = _page.Locator(".modal-actions").GetByText("Add staff", new LocatorGetByTextOptions { Exact = true });

        // Empty form - genuinely disabled, not just visually styled to look that way.
        await Assertions.Expect(saveButton).ToBeDisabledAsync();

        var (aadhaar, phone, bankAccount, name) = GenerateUniqueTestData();
        await FillMinimalRequiredFieldsAsync(_page, name, aadhaar, phone, bankAccount);

        await Assertions.Expect(saveButton).ToBeEnabledAsync();
    }

    [Fact]
    public async Task AddStaff_DuplicateName_ShowsWarningInterstitial_NotAnImmediateBlock()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/staff");

        // First, a real staff member to collide with.
        var first = GenerateUniqueTestData("E2E Duplicate Target");
        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await FillMinimalRequiredFieldsAsync(_page, first.Name, first.Aadhaar, first.Phone, first.BankAccount);
        await _page.Locator(".modal-actions").GetByText("Add staff", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.GetByText("Staff profile created")).ToBeVisibleAsync();
        await _page.GetByText("Done").ClickAsync();

        // Genuinely wait for the dialog to finish closing - not just for
        // the click to register - before trying to reopen it. Closing
        // is itself an async Blazor Server round-trip (VisibleChanged.InvokeAsync),
        // and the previous dialog's modal-backdrop needs to actually
        // leave the DOM before a fresh one can reliably be opened again.
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        // Now a second, genuinely different person - fresh, unique
        // Aadhaar/phone/bank account - but with the EXACT same name, so
        // only the name collides.
        var secondUniqueData = GenerateUniqueTestData(); // .Name deliberately discarded - reusing first.Name instead

        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await FillMinimalRequiredFieldsAsync(_page, first.Name, secondUniqueData.Aadhaar, secondUniqueData.Phone, secondUniqueData.BankAccount);
        await _page.Locator(".modal-actions").GetByText("Add staff", new LocatorGetByTextOptions { Exact = true }).ClickAsync();

        // The warning interstitial, not an outright block - this is
        // specifically an E2E-worthy check, since it's a multi-stage UI
        // transition, not just a single business-rule outcome.
        await Assertions.Expect(_page.GetByText("Possible duplicate")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Create anyway")).ToBeVisibleAsync();
        await Assertions.Expect(_page.GetByText("Go back")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddStaff_DuplicateAadhaar_ShowsLiveCollisionError()
    {
        // Genuinely different from AddStaff_InvalidAadhaar_ShowsLiveErrorWithoutClickingSave -
        // that one checks FORMAT only. This checks COLLISION: a real,
        // async database round-trip triggered on blur, confirming the
        // live uniqueness check actually works end-to-end in a real
        // browser, not just the underlying service logic (already
        // covered at the unit level).
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/staff");

        var first = GenerateUniqueTestData("E2E Aadhaar Collision Target");
        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await FillMinimalRequiredFieldsAsync(_page, first.Name, first.Aadhaar, first.Phone, first.BankAccount);
        await _page.Locator(".modal-actions").GetByText("Add staff", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.GetByText("Staff profile created")).ToBeVisibleAsync();
        await _page.GetByText("Done").ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        // A second, genuinely different person - except reusing the
        // exact same Aadhaar deliberately.
        var second = GenerateUniqueTestData("E2E Different Aadhaar Person");
        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await _page.Locator("div.form-row:has(label:text('Full name *')) input").FillAsync(second.Name);
        await _page.GetByPlaceholder("12-digit number").FillAsync(first.Aadhaar); // the collision
        await _page.GetByPlaceholder("12-digit number").PressAsync("Tab");

        await Assertions.Expect(_page.GetByText($"Already on file for \"{first.Name.ToUpperInvariant()}\".")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddStaff_BankAccountMismatch_ShowsLiveError()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/staff");
        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();

        await _page.GetByPlaceholder("Type account number").FillAsync("123456789012");
        await _page.GetByPlaceholder("Must match exactly").FillAsync("999999999999");

        // A live computed condition (_accountNumber != _accountNumberConfirm),
        // reacting on every keystroke - no blur/Tab needed, unlike the
        // async uniqueness checks elsewhere on this form.
        await Assertions.Expect(_page.GetByText("Account numbers do not match.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddStaff_InvalidPan_ShowsLiveFormatError()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/staff");
        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();

        await _page.GetByPlaceholder("ABCDE1234F — leave blank if not available").FillAsync("NOTAVALIDPAN");
        await _page.GetByPlaceholder("ABCDE1234F — leave blank if not available").PressAsync("Tab");

        await Assertions.Expect(_page.GetByText("Doesn't match the standard PAN format (5 letters, 4 digits, 1 letter).")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddStaff_InvalidPhone_ShowsLiveFormatError()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/staff");
        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();

        // Starts with 5 - not a valid Indian mobile prefix (must be 6-9).
        await _page.GetByPlaceholder("10-digit mobile").FillAsync("5123456789");

        await Assertions.Expect(_page.GetByText("Must be a valid 10-digit Indian mobile number, starting with 6, 7, 8, or 9.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddStaff_InvalidIfsc_ShowsLiveFormatError()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/staff");
        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();

        await _page.GetByPlaceholder("e.g. SBIN0001234").FillAsync("BADFORMAT");
        await _page.GetByPlaceholder("e.g. SBIN0001234").PressAsync("Tab");

        await Assertions.Expect(_page.GetByText("Must be 11 characters: 4 letters, then 0, then 6 letters/digits.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddStaff_EnableSignIn_RevealsUpnField_AndAutoFillsFromUsername()
    {
        // Deliberately stops here, never clicking "Add staff" with sign-in
        // enabled - that would provision a REAL Entra account against the
        // real configured tenant, which is specifically Layer 3's
        // responsibility (deliberately separate, deliberately infrequent),
        // not something to fold into Layer 2 casually. This test checks
        // only the form's own client-side interactivity - the checkbox
        // revealing a field and auto-filling it - not the actual
        // provisioning outcome.
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/staff");
        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();

        var username = $"e2e.formtest.{(uint)Guid.NewGuid().GetHashCode()}";
        // Placeholder-based, matching the same proven pattern already
        // used reliably for every other field on this form - two
        // different label-text-matching approaches both failed for this
        // specific field for reasons never fully pinned down, so this
        // switches to the one selector strategy that's actually held up
        // consistently throughout this whole file. "e.g. a.naveen" is
        // the full, distinct placeholder text - safe from ambiguity with
        // the UPN field's shorter "a.naveen" placeholder, since substring
        // matching only goes one direction.
        var usernameField = _page.GetByPlaceholder("e.g. a.naveen");
        await usernameField.FillAsync(username);

        // The UPN field doesn't exist at all until this checkbox is checked.
        var upnField = _page.Locator("div.form-row:has(label:text('Sign-in username')) input").First;
        await Assertions.Expect(upnField).Not.ToBeVisibleAsync();

        await _page.Locator("label:has(span:text('Enable sign-in for this person')) input[type='checkbox']").CheckAsync();

        await Assertions.Expect(upnField).ToBeVisibleAsync();
        await Assertions.Expect(upnField).ToHaveValueAsync(username);

        await _page.GetByText("Cancel").ClickAsync();
    }

    [Fact]
    public async Task AddStaff_Cancel_ClosesWithoutCreatingAnything()
    {
        await AuthHelper.SignInAsync(_page);
        await _page.GotoAsync($"{E2EConfig.BaseUrl}/staff");
        await _page.GetByText("Add staff", new PageGetByTextOptions { Exact = false }).ClickAsync();

        var (aadhaar, phone, bankAccount, name) = GenerateUniqueTestData("E2E Should Never Be Created");
        await FillMinimalRequiredFieldsAsync(_page, name, aadhaar, phone, bankAccount);

        await _page.GetByText("Cancel").ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        // The name this test deliberately never saved should be nowhere
        // on the page - confirms Cancel genuinely discards the form
        // rather than saving anything partial.
        await Assertions.Expect(_page.GetByText(name.ToUpperInvariant())).Not.ToBeVisibleAsync();
    }
}
