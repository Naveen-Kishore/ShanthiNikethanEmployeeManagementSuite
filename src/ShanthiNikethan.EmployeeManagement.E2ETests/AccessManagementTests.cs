using Microsoft.Playwright;
using ShanthiNikethan.EmployeeManagement.E2ETests.TestHelpers;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.E2ETests;

public class AccessManagementTests : IClassFixture<PlaywrightFixture>, IAsyncLifetime
{
    private readonly PlaywrightFixture _fixture;
    private IPage _page = null!;

    public AccessManagementTests(PlaywrightFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => _page = await _fixture.NewIsolatedPageAsync();

    public async Task DisposeAsync() => await _page.Context.CloseAsync();

    private async Task NavigateToAccessManagementAsync()
    {
        await _page.GetByText("Administration", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await _page.GetByText("Access Management", new PageGetByTextOptions { Exact = true }).ClickAsync();
    }

    /// <summary>
    /// e2e.testuser's own account and role group ("E2E Test Admin") live
    /// in this exact table - every test in this file either creates its
    /// own fresh, uniquely-named role group/member to work with, or only
    /// reads existing data. None ever touches that account or its role
    /// group - a mistake there could lock every other test in this whole
    /// suite out of signing in at all.
    /// </summary>
    private async Task<string> CreateRoleGroupAsync(string namePrefix)
    {
        var name = $"{namePrefix} {(uint)Guid.NewGuid().GetHashCode()}";
        await _page.GetByText("Create role group", new PageGetByTextOptions { Exact = false }).ClickAsync();
        await _page.Locator(".modal").GetByPlaceholder("e.g. Accounts Assistant").FillAsync(name);
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        // One permission is enough to prove the create flow itself works -
        // which specific one doesn't matter for these tests.
        await _page.Locator(".modal input[type='checkbox']").First.CheckAsync();
        await _page.Locator(".modal-actions").GetByText("Create", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();
        return name;
    }

    [Fact]
    public async Task CreateRoleGroup_HappyPath_AppearsInTheList()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();

        var name = await CreateRoleGroupAsync("E2E Create Test");

        await Assertions.Expect(_page.GetByText(name)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task RoleGroupDrawer_TogglingAPermission_UpdatesTheListsOwnCount()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();
        var name = await CreateRoleGroupAsync("E2E Edit Perms");

        var row = _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name });
        var countBefore = await row.Locator("td").Last.InnerTextAsync();

        await row.ClickAsync();
        // Scoped to .drawer - the list's own "Roles" column header uses
        // this exact same text and stays in the DOM behind the drawer
        // overlay, the same class of collision AuditLog's "IP Address"
        // ambiguity turned out to be.
        await _page.Locator(".drawer").GetByText("Roles", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        // Toggle a second permission on top of the one already selected
        // at creation - the list's own permission count is the simplest,
        // most direct way to confirm the save genuinely took effect.
        // Targets the first genuinely UNCHECKED box, rather than
        // assuming a specific index aligns between the create modal and
        // this drawer - CheckAsync on an already-checked box is a no-op,
        // which would silently defeat this test if the positional
        // assumption ever turned out to be wrong.
        await _page.Locator(".drawer input[type='checkbox']:not(:checked)").First.CheckAsync();
        await _page.Locator(".drawer-footer").GetByText("Save").ClickAsync();

        // Auto-retrying assertion rather than one immediate read - Save
        // triggers an async server round-trip, and reading the count on
        // the very next line risked racing ahead of that completing.
        await Assertions.Expect(row.Locator("td").Last).Not.ToHaveTextAsync(countBefore);
    }

    [Fact]
    public async Task BuiltInRoleGroup_FieldsAreDisabled_AndCannotBeDeleted()
    {
        // Purely a read-only confirmation - no modification is attempted
        // anywhere in this test, deliberately, given the stakes of
        // touching a real, in-use built-in role group.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();

        var builtInRow = _page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = "Yes" }).First;
        await builtInRow.ClickAsync();

        // Scoped to .drawer - the table's own "Built-in" column header
        // uses this exact same text, the same class of collision as
        // every other drawer-over-table overlay case this session.
        await Assertions.Expect(_page.Locator(".drawer").GetByText("Built-in", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator(".drawer input[type='text']").First).ToBeDisabledAsync();
        await Assertions.Expect(_page.Locator(".drawer-footer").GetByText("Delete")).Not.ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator(".drawer-footer").GetByText("Save")).ToBeDisabledAsync();
    }

    [Fact]
    public async Task AddMember_WithLocalLogin_AppearsInTheMembersList()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();
        await _page.GetByText("Users", new PageGetByTextOptions { Exact = true }).ClickAsync();

        var name = $"E2E Member {(uint)Guid.NewGuid().GetHashCode()}";
        await _page.GetByText("Add user", new PageGetByTextOptions { Exact = false }).First.ClickAsync();
        await _page.Locator(".modal").GetByPlaceholder("Full name").FillAsync(name);
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        // Role Group has no empty default option, so it LOOKS
        // pre-selected on screen via native browser rendering - but the
        // underlying Blazor variable isn't actually bound until an
        // explicit selection fires the change event. Confirmed directly
        // in the real code: NewUserIsValid requires _newUserRoleGroupId
        // != Guid.Empty, and SaveUser's own validation checks the same.
        await _page.Locator(".modal select").Nth(1).SelectOptionAsync(new SelectOptionValue { Index = 0 });
        await _page.Locator(".modal input[type='checkbox']").CheckAsync();
        await _page.GetByPlaceholder("e.g. officeadmin2").FillAsync($"e2emember{(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        await _page.GetByPlaceholder("At least 8 characters").FillAsync("E2ETestPass123");
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        await _page.Locator(".modal-actions").GetByText("Add user", new LocatorGetByTextOptions { Exact = true }).ClickAsync();

        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();
        await Assertions.Expect(_page.GetByText(name)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddMember_WithoutASignInMethod_KeepsSubmitDisabled()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();
        await _page.GetByText("Users", new PageGetByTextOptions { Exact = true }).ClickAsync();

        await _page.GetByText("Add user", new PageGetByTextOptions { Exact = false }).First.ClickAsync();
        // Display name only - neither an Entra UPN nor local login enabled.
        await _page.Locator(".modal").GetByPlaceholder("Full name").FillAsync($"E2E No SignIn Method {(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput

        await Assertions.Expect(_page.GetByText("This account needs a way to sign in")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator(".modal-actions").GetByText("Add user", new LocatorGetByTextOptions { Exact = true })).ToBeDisabledAsync();
    }

    [Fact]
    public async Task MyPermissions_ShowsTheSignedInUsersOwnRoleGroup()
    {
        // Purely read-only - shows CurrentUser's own data directly, no
        // form fields to touch and nothing to accidentally modify here.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();

        await _page.GetByText("My Permissions", new PageGetByTextOptions { Exact = true }).ClickAsync();

        await Assertions.Expect(_page.GetByText("Signed in as")).ToBeVisibleAsync();
        // Confirmed real, not a placeholder - e2e.testuser genuinely has
        // an assigned role group ("E2E Test Admin"), so this should never
        // show the "— None assigned —" fallback state.
        await Assertions.Expect(_page.GetByText("— None assigned —")).Not.ToBeVisibleAsync();
    }

    /// <summary>
    /// Creates a member, assigned to a specific role group by NAME rather
    /// than by guessing its GUID option value - the same "find the option
    /// by its visible text, read its real value" technique proven safe
    /// in the Leave module's staff dropdown, for the same reason: a
    /// role group's option value is its real database ID, not something
    /// a test can predict ahead of time.
    /// </summary>
    private async Task<string> CreateMemberInRoleGroupAsync(string roleGroupName)
    {
        var name = $"E2E Member {(uint)Guid.NewGuid().GetHashCode()}";
        await _page.GetByText("Users", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await _page.GetByText("Add user", new PageGetByTextOptions { Exact = false }).First.ClickAsync();
        await _page.Locator(".modal").GetByPlaceholder("Full name").FillAsync(name);
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput

        var roleGroupSelect = _page.Locator(".modal select").Nth(1); // 0 = staff link, 1 = role group
        var option = roleGroupSelect.Locator("option").Filter(new LocatorFilterOptions { HasText = roleGroupName });
        var value = await option.GetAttributeAsync("value");
        await roleGroupSelect.SelectOptionAsync(value!);
        // Auto-retrying, rather than assuming the selection landed
        // instantly - SelectOptionAsync can return before the server's
        // own round-trip has genuinely settled, which may be what made
        // this specific helper intermittently unreliable before.
        await Assertions.Expect(roleGroupSelect).ToHaveValueAsync(value!);

        await _page.Locator(".modal input[type='checkbox']").CheckAsync();
        await _page.GetByPlaceholder("e.g. officeadmin2").FillAsync($"e2emember{(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        await _page.GetByPlaceholder("At least 8 characters").FillAsync("E2ETestPass123");
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput

        await _page.Locator(".modal-actions").GetByText("Add user", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();
        return name;
    }

    [Fact]
    public async Task DeactivateThenReactivate_TogglesTheInactiveBadge()
    {
        // Genuinely distinct from Delete - a soft, reversible disable
        // rather than permanent removal, confirmed by its own separate
        // button and the fact the drawer stays open afterward showing
        // updated state, rather than closing like Delete does.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();
        await _page.GetByText("Users", new PageGetByTextOptions { Exact = true }).ClickAsync();
        var name = $"E2E Deactivate Test {(uint)Guid.NewGuid().GetHashCode()}";
        await _page.GetByText("Add user", new PageGetByTextOptions { Exact = false }).First.ClickAsync();
        await _page.Locator(".modal").GetByPlaceholder("Full name").FillAsync(name);
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        // Role Group has no empty default option, so it LOOKS
        // pre-selected on screen via native browser rendering - but the
        // underlying Blazor variable isn't actually bound until an
        // explicit selection fires the change event. Confirmed directly
        // in the real code: NewUserIsValid requires _newUserRoleGroupId
        // != Guid.Empty, and SaveUser's own validation checks the same.
        await _page.Locator(".modal select").Nth(1).SelectOptionAsync(new SelectOptionValue { Index = 0 });
        await _page.Locator(".modal input[type='checkbox']").CheckAsync();
        await _page.GetByPlaceholder("e.g. officeadmin2").FillAsync($"e2edeact{(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        await _page.GetByPlaceholder("At least 8 characters").FillAsync("E2ETestPass123");
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        await _page.Locator(".modal-actions").GetByText("Add user", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name }).ClickAsync();
        await _page.Locator(".drawer-footer").GetByText("Deactivate").ClickAsync();

        await Assertions.Expect(_page.Locator(".drawer").GetByText("Inactive")).ToBeVisibleAsync();

        await _page.Locator(".drawer-footer").GetByText("Reactivate").ClickAsync();

        await Assertions.Expect(_page.Locator(".drawer").GetByText("Inactive")).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task DeleteMember_RemovesItFromTheList()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();

        await _page.GetByText("Users", new PageGetByTextOptions { Exact = true }).ClickAsync();
        var name = $"E2E Delete Member {(uint)Guid.NewGuid().GetHashCode()}";
        await _page.GetByText("Add user", new PageGetByTextOptions { Exact = false }).First.ClickAsync();
        await _page.Locator(".modal").GetByPlaceholder("Full name").FillAsync(name);
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        // Role Group has no empty default option, so it LOOKS
        // pre-selected on screen via native browser rendering - but the
        // underlying Blazor variable isn't actually bound until an
        // explicit selection fires the change event. Confirmed directly
        // in the real code: NewUserIsValid requires _newUserRoleGroupId
        // != Guid.Empty, and SaveUser's own validation checks the same.
        await _page.Locator(".modal select").Nth(1).SelectOptionAsync(new SelectOptionValue { Index = 0 });
        await _page.Locator(".modal input[type='checkbox']").CheckAsync();
        await _page.GetByPlaceholder("e.g. officeadmin2").FillAsync($"e2edel{(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        await _page.GetByPlaceholder("At least 8 characters").FillAsync("E2ETestPass123");
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        await _page.Locator(".modal-actions").GetByText("Add user", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name }).ClickAsync();
        await _page.Locator(".drawer-footer").GetByText("Delete member").ClickAsync();
        await Assertions.Expect(_page.GetByText("This permanently removes the account")).ToBeVisibleAsync();
        await _page.Locator(".modal-actions").GetByText("Delete permanently").ClickAsync();

        // Scoped to the table row specifically - a bare text match could
        // collide with the confirm dialog's own heading (Delete "name"?),
        // which contains this exact same name.
        await Assertions.Expect(_page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = name })).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task DeleteRoleGroup_WithNoMembers_Succeeds()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();
        var name = await CreateRoleGroupAsync("E2E Delete Empty Group");

        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = name }).ClickAsync();
        await _page.Locator(".drawer-footer").GetByText("Delete", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await _page.Locator(".modal-actions").GetByText("Delete permanently").ClickAsync();

        // Same collision risk as DeleteMember above - scoped to the row
        // specifically, not a bare text match against the whole page.
        await Assertions.Expect(_page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = name })).Not.ToBeVisibleAsync();
    }

    [Fact]
    public async Task DeleteRoleGroup_WithAssignedMembers_IsBlocked()
    {
        // The real safeguard this test exists to confirm: deletion must
        // fail while any member still belongs to the group, not silently
        // orphan that member's role assignment.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();
        var groupName = await CreateRoleGroupAsync("E2E Blocked Delete Group");
        await CreateMemberInRoleGroupAsync(groupName);

        await _page.GetByText("Role Groups", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = groupName }).ClickAsync();
        await _page.Locator(".drawer-footer").GetByText("Delete", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await _page.Locator(".modal-actions").GetByText("Delete permanently").ClickAsync();

        // Still present - the delete was genuinely rejected, not just
        // slow, and an error explaining why is shown in the confirm dialog.
        // Scoped to the table row specifically - the confirm dialog's own
        // heading (Delete "name"?) contains this exact same text while
        // still open, the same collision already fixed twice elsewhere
        // in this file for the other Delete tests.
        await Assertions.Expect(_page.Locator(".modal .alert.danger")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = groupName })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task RoleGroupDrawer_MembersTab_ShowsAssignedMember_AndEditShortcutOpensTheirDrawer()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();
        var groupName = await CreateRoleGroupAsync("E2E Members Tab Group");
        var memberName = await CreateMemberInRoleGroupAsync(groupName);

        await _page.GetByText("Role Groups", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = groupName }).ClickAsync();
        await _page.Locator(".drawer").GetByText("Members", new LocatorGetByTextOptions { Exact = true }).ClickAsync();

        await Assertions.Expect(_page.Locator(".drawer").GetByText(memberName)).ToBeVisibleAsync();

        // Clicking the edit shortcut closes THIS drawer and opens the
        // member's own drawer instead - confirmed directly in the code:
        // { _drawerRoleGroup = null; OpenUserDrawer(m); }
        await _page.GetByTitle("Edit member").ClickAsync();

        await Assertions.Expect(_page.Locator(".drawer h2")).ToHaveTextAsync(memberName);
    }

    [Fact]
    public async Task RolesTab_ShowsTheFixedReferenceList()
    {
        // Purely read-only - the Roles tab is a fixed reference list,
        // not editable data, so nothing here modifies anything.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();

        // Scoped to .filter-tabs - the Role Groups table (visible by
        // default at this point) has its own "Roles" column header,
        // the same class of collision as the drawer's tab bar, just one
        // level up.
        await _page.Locator(".filter-tabs").GetByText("Roles", new LocatorGetByTextOptions { Exact = true }).ClickAsync();

        await Assertions.Expect(_page.GetByText("Every role the app actually checks for")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator("table.grid tbody tr").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task EditMember_ReassigningRoleGroup_UpdatesTheListsOwnColumn()
    {
        // SaveDrawerUser is a genuinely separate code path from create's
        // SaveUser - untested until now. Two fresh role groups (source
        // and target) rather than reusing an existing real one, same
        // safety principle as everywhere else in this file.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();
        var sourceGroup = await CreateRoleGroupAsync("E2E Reassign Source");
        var targetGroup = await CreateRoleGroupAsync("E2E Reassign Target");
        var memberName = await CreateMemberInRoleGroupAsync(sourceGroup);

        var row = _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = memberName });
        await row.ClickAsync();

        var roleGroupSelect = _page.Locator(".drawer select").Nth(1); // 0 = staff link, 1 = role group
        var option = roleGroupSelect.Locator("option").Filter(new LocatorFilterOptions { HasText = targetGroup });
        var value = await option.GetAttributeAsync("value");
        await roleGroupSelect.SelectOptionAsync(value!);
        await _page.Locator(".drawer-footer").GetByText("Save").ClickAsync();

        // The list's own "Role group" column is the simplest, most
        // direct confirmation the reassignment genuinely persisted.
        await Assertions.Expect(row.Locator("td").Nth(2)).ToHaveTextAsync(targetGroup);
    }

    [Fact]
    public async Task EditMember_EnablingLocalLoginWithoutAUsername_ShowsAValidationError()
    {
        // A validation guard specific to the drawer's own SaveDrawerUser,
        // distinct from the create dialog's separate validation logic.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();
        // A member created WITHOUT local login, via Entra UPN instead -
        // the drawer's local-login checkbox needs to start unchecked for
        // this test to exercise turning it on.
        var memberName = $"E2E No Local Login {(uint)Guid.NewGuid().GetHashCode()}";
        await _page.GetByText("Users", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await _page.GetByText("Add user", new PageGetByTextOptions { Exact = false }).First.ClickAsync();
        await _page.Locator(".modal").GetByPlaceholder("Full name").FillAsync(memberName);
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        // Role Group has no empty default option, so it LOOKS
        // pre-selected via native browser rendering - but the underlying
        // Blazor variable isn't bound until an explicit selection fires.
        await _page.Locator(".modal select").Nth(1).SelectOptionAsync(new SelectOptionValue { Index = 0 });
        await _page.GetByPlaceholder("Leave blank if not using Entra sign-in").FillAsync($"e2e.{(uint)Guid.NewGuid().GetHashCode()}@school.onmicrosoft.com");
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        await _page.Locator(".modal-actions").GetByText("Add user", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = memberName }).ClickAsync();
        await _page.Locator(".drawer input[type='checkbox']").CheckAsync();
        // Deliberately leaving the now-revealed username field empty.
        await _page.Locator(".drawer-footer").GetByText("Save").ClickAsync();

        await Assertions.Expect(_page.GetByText("Local login is enabled but has no username set.")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task EditMember_ResettingPasswordTooShort_ShowsAValidationError()
    {
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();
        var groupName = await CreateRoleGroupAsync("E2E Short Password Group");
        var memberName = await CreateMemberInRoleGroupAsync(groupName);

        await _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = memberName }).ClickAsync();
        await _page.GetByPlaceholder("Leave blank to keep the current password").FillAsync("short");
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        await _page.Locator(".drawer-footer").GetByText("Save").ClickAsync();

        await Assertions.Expect(_page.GetByText("New password must be at least 8 characters")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task EditMember_PastExpiryDate_ShowsExpiredInTheList()
    {
        // The drawer's own expiry input has no min attribute (unlike the
        // create dialog's, which enforces today-or-later) - a past date
        // is genuinely settable here, letting this test reach the
        // "isExpired" styling branch the create flow can't produce.
        await AuthHelper.SignInAsync(_page);
        await NavigateToAccessManagementAsync();
        var groupName = await CreateRoleGroupAsync("E2E Expiry Group");
        var memberName = await CreateMemberInRoleGroupAsync(groupName);

        var row = _page.Locator("tr").Filter(new LocatorFilterOptions { HasText = memberName });
        await row.ClickAsync();
        await _page.Locator(".drawer input[type='date']").FillAsync("2020-01-01");
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput
        await _page.Locator(".drawer-footer").GetByText("Save").ClickAsync();

        await Assertions.Expect(row.GetByText("(expired)")).ToBeVisibleAsync();
    }

    /// <summary>
    /// Every test above uses a standalone member, unlinked to any staff
    /// profile - a genuinely distinct code path, IsLinkedAccount, was
    /// entirely untested until now. Reuses the same minimal staff-
    /// creation flow proven throughout AddStaffTests.cs.
    /// </summary>
    private async Task<string> CreateStaffMemberAsync()
    {
        var uniqueNum = (uint)Guid.NewGuid().GetHashCode();
        var name = $"E2E Admin Staff {uniqueNum}";
        var tenDigits = uniqueNum.ToString("D10");
        var aadhaar = "99" + tenDigits;
        var phone = "9" + tenDigits.Substring(0, 9);
        var bankAccount = "E2E" + uniqueNum;

        // Scoped to a.nav-item - the Dashboard (where sign-in lands)
        // apparently has its own "Staff Directory" quick-action shortcut,
        // the exact same collision Payroll's nav link hit earlier this
        // session. Same proven fix applies directly.
        await _page.Locator("a.nav-item", new PageLocatorOptions { HasTextString = "Staff Directory" }).ClickAsync();
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
    public async Task StaffWithoutAccount_ShowsAsItsOwnDistinctRowInTheUsersList()
    {
        // A staff member deliberately created WITHOUT a linked user
        // account - confirms the newer "no sign-in configured" row type
        // this module renders separately from real accounts.
        await AuthHelper.SignInAsync(_page);
        var staffName = await CreateStaffMemberAsync();

        await NavigateToAccessManagementAsync();
        await _page.GetByText("Users", new PageGetByTextOptions { Exact = true }).ClickAsync();

        var row = _page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = staffName });
        await Assertions.Expect(row).ToBeVisibleAsync();
        await Assertions.Expect(row.GetByText("No sign-in configured")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AddMember_LinkedToAStaffProfile_LocksNameInTheDrawer()
    {
        await AuthHelper.SignInAsync(_page);
        var staffName = await CreateStaffMemberAsync();

        await NavigateToAccessManagementAsync();
        await _page.GetByText("Users", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await _page.GetByText("Add user", new PageGetByTextOptions { Exact = false }).First.ClickAsync();
        await _page.Locator(".modal").GetByPlaceholder("Full name").FillAsync($"E2E Linked {(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Keyboard.PressAsync("Tab"); // blur - plain @bind updates on change, not oninput

        var staffLinkSelect = _page.Locator(".modal select").First; // 0 = staff link, 1 = role group
        var staffOption = staffLinkSelect.Locator("option").Filter(new LocatorFilterOptions { HasText = staffName });
        var staffValue = await staffOption.GetAttributeAsync("value");
        await staffLinkSelect.SelectOptionAsync(staffValue!);
        await Assertions.Expect(staffLinkSelect).ToHaveValueAsync(staffValue!);

        var roleGroupSelect = _page.Locator(".modal select").Nth(1);
        await roleGroupSelect.SelectOptionAsync(new SelectOptionValue { Index = 0 });

        var upn = $"e2e.{(uint)Guid.NewGuid().GetHashCode()}@school.onmicrosoft.com";
        await _page.GetByPlaceholder("Leave blank if not using Entra sign-in").FillAsync(upn);
        await _page.Keyboard.PressAsync("Tab");
        await _page.Locator(".modal-actions").GetByText("Add user", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        await _page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = staffName }).ClickAsync();

        // Confirmed directly in the real code: IsLinkedAccount locks the
        // Name field via readonly specifically, not disabled - a
        // genuinely different HTML attribute, checked accordingly here.
        await Assertions.Expect(_page.Locator(".drawer input[type='text']").First).ToHaveAttributeAsync("readonly", "");
        await Assertions.Expect(_page.GetByText("locked here to keep them in sync")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DeleteLinkedMember_ShowsTheStaffProfileSpecificWarning()
    {
        // Confirmed directly in the real code: linked accounts get a
        // genuinely different confirm message and button label ("Remove",
        // not "Delete permanently") than the standalone case every other
        // Delete test in this file already covers.
        await AuthHelper.SignInAsync(_page);
        var staffName = await CreateStaffMemberAsync();

        await NavigateToAccessManagementAsync();
        await _page.GetByText("Users", new PageGetByTextOptions { Exact = true }).ClickAsync();
        await _page.GetByText("Add user", new PageGetByTextOptions { Exact = false }).First.ClickAsync();
        await _page.Locator(".modal").GetByPlaceholder("Full name").FillAsync($"E2E Linked Delete {(uint)Guid.NewGuid().GetHashCode()}");
        await _page.Keyboard.PressAsync("Tab");

        var staffLinkSelect = _page.Locator(".modal select").First;
        var staffOption = staffLinkSelect.Locator("option").Filter(new LocatorFilterOptions { HasText = staffName });
        var staffValue = await staffOption.GetAttributeAsync("value");
        await staffLinkSelect.SelectOptionAsync(staffValue!);
        await Assertions.Expect(staffLinkSelect).ToHaveValueAsync(staffValue!);

        await _page.Locator(".modal select").Nth(1).SelectOptionAsync(new SelectOptionValue { Index = 0 });
        await _page.GetByPlaceholder("Leave blank if not using Entra sign-in").FillAsync($"e2e.{(uint)Guid.NewGuid().GetHashCode()}@school.onmicrosoft.com");
        await _page.Keyboard.PressAsync("Tab");
        await _page.Locator(".modal-actions").GetByText("Add user", new LocatorGetByTextOptions { Exact = true }).ClickAsync();
        await Assertions.Expect(_page.Locator(".modal-backdrop")).ToBeHiddenAsync();

        await _page.Locator("table.grid tbody tr").Filter(new LocatorFilterOptions { HasText = staffName }).ClickAsync();
        await _page.Locator(".drawer-footer").GetByText("Delete member").ClickAsync();

        await Assertions.Expect(_page.GetByText("moves that staff profile to the soft-deleted list")).ToBeVisibleAsync();
        await Assertions.Expect(_page.Locator(".modal-actions").GetByText("Remove", new LocatorGetByTextOptions { Exact = true })).ToBeVisibleAsync();
    }
}
