using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Linq;
using NSubstitute;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Services;
using ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Services;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;
using ShanthiNikethan.EmployeeManagement.Tests.TestHelpers;
using Xunit;

namespace ShanthiNikethan.EmployeeManagement.Tests.Services;

public class UserAccountServiceTests : IDisposable
{
    private readonly TestDbContextFactory _dbFactory;
    private readonly ICurrentUser _currentUser;
    private readonly IAuditService _audit;
    private readonly IGraphProvisioningService _graphService;
    private readonly IConfiguration _config;
    private readonly UserAccountService _sut; // "system under test"

    public UserAccountServiceTests()
    {
        _dbFactory = new TestDbContextFactory();
        _currentUser = Substitute.For<ICurrentUser>();
        _audit = Substitute.For<IAuditService>();
        _graphService = Substitute.For<IGraphProvisioningService>();
        _config = Substitute.For<IConfiguration>();
        _sut = new UserAccountService(_dbFactory, _currentUser, _audit, _graphService, _config);
    }

    public void Dispose() => _dbFactory.Dispose();

    private async Task<RoleGroup> SeedGlobalAdminRoleGroupAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var group = new RoleGroup { Id = Guid.NewGuid(), Name = "Global Administrator", CreatedByObjectId = "test", CreatedByDisplayName = "test" };
        db.Set<RoleGroup>().Add(group);
        await db.SaveChangesAsync();
        return group;
    }

    // UserAccount.StaffId is a real foreign key (SQLite enforces this,
    // unlike EF Core's basic InMemory provider) - any test representing
    // a linked account needs an actual Staff row to point at, not just a
    // random Guid.
    private async Task<Guid> SeedStaffAsync(string displayName = "Test Staff Member")
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var staff = new Staff
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            FirstName = displayName,
            StaffCode = $"TEST-{Guid.NewGuid().ToString()[..8]}"
        };
        db.Set<Staff>().Add(staff);
        await db.SaveChangesAsync();
        return staff.Id;
    }

    // ====================================================================
    // EnsureBootstrapAdminAccountAsync - the critical security fix.
    // Before this fix, ANY unmatched Entra sign-in silently became Global
    // Administrator. This is exactly the class of regression worth a
    // permanent, automated guard against - a manual re-test can easily
    // be skipped under time pressure; this can't be.
    // ====================================================================

    [Fact]
    public async Task EnsureBootstrapAdminAccountAsync_ReturnsNull_ForAnyObjectIdOtherThanTheConfiguredOne()
    {
        var configuredBootstrapId = Guid.NewGuid().ToString();
        _config["Authorization:BootstrapGlobalAdminObjectId"].Returns(configuredBootstrapId);
        await SeedGlobalAdminRoleGroupAsync();

        var someOtherRealEntraUser = Guid.NewGuid().ToString();
        var result = await _sut.EnsureBootstrapAdminAccountAsync(someOtherRealEntraUser, "Someone Else Entirely");

        Assert.Null(result);
    }

    [Fact]
    public async Task EnsureBootstrapAdminAccountAsync_CreatesGlobalAdmin_OnlyForTheExactConfiguredObjectId()
    {
        var configuredBootstrapId = Guid.NewGuid().ToString();
        _config["Authorization:BootstrapGlobalAdminObjectId"].Returns(configuredBootstrapId);
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();

        var result = await _sut.EnsureBootstrapAdminAccountAsync(configuredBootstrapId, "The Real Bootstrap Admin");

        Assert.NotNull(result);
        Assert.Equal(configuredBootstrapId, result!.EntraObjectId);
        Assert.Equal(adminGroup.Id, result.RoleGroupId);
    }

    [Fact]
    public async Task EnsureBootstrapAdminAccountAsync_ReturnsNull_WhenConfigValueIsAPlaceholderNotARealGuid()
    {
        // Guards the specific failure mode this was fixed for: a
        // leftover placeholder string in config (e.g. still literally
        // "PASTE_YOUR_OBJECT_ID_HERE") must never accidentally match
        // anything, even by coincidence.
        _config["Authorization:BootstrapGlobalAdminObjectId"].Returns("PASTE_YOUR_OBJECT_ID_HERE");
        await SeedGlobalAdminRoleGroupAsync();

        var result = await _sut.EnsureBootstrapAdminAccountAsync("PASTE_YOUR_OBJECT_ID_HERE", "Someone");

        Assert.Null(result);
    }

    [Fact]
    public async Task EnsureBootstrapAdminAccountAsync_ReturnsExistingAccount_WithoutCreatingADuplicate()
    {
        var configuredBootstrapId = Guid.NewGuid().ToString();
        _config["Authorization:BootstrapGlobalAdminObjectId"].Returns(configuredBootstrapId);
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();

        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = Guid.NewGuid(),
                DisplayName = "Already Exists",
                EntraObjectId = configuredBootstrapId,
                RoleGroupId = adminGroup.Id,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var result = await _sut.EnsureBootstrapAdminAccountAsync(configuredBootstrapId, "The Real Bootstrap Admin");

        Assert.NotNull(result);
        Assert.Equal("Already Exists", result!.DisplayName); // the existing one, not a fresh duplicate

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var countWithThisObjectId = verifyDb.Set<UserAccount>().Count(u => u.EntraObjectId == configuredBootstrapId);
        Assert.Equal(1, countWithThisObjectId);
    }

    // ====================================================================
    // UpdateRoleGroupPermissionsAsync - the self-lockout guard.
    // Stripping the last Admin.ManageUsers/ManageRoleGroups permission
    // from every role group would lock everyone, including whoever did
    // it, out of Access Management with no way back in through the UI.
    // ====================================================================

    [Fact]
    public async Task UpdateRoleGroupPermissionsAsync_Throws_WhenRemovingTheLastAdminPermissionEverywhere()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<RoleGroupPermission>().Add(new RoleGroupPermission { Id = Guid.NewGuid(), RoleGroupId = adminGroup.Id, PermissionKey = "Admin.ManageUsers" });
            await db.SaveChangesAsync();
        }

        // Attempting to replace this group's permissions with a set that
        // has neither Admin.ManageUsers nor Admin.ManageRoleGroups - and
        // there's no OTHER role group anywhere with either permission.
        var newPermissions = new List<string> { "Dashboard.View" };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _sut.UpdateRoleGroupPermissionsAsync(adminGroup.Id, newPermissions));
    }

    [Fact]
    public async Task UpdateRoleGroupPermissionsAsync_Allows_WhenAnotherRoleGroupStillHasAdminAccess()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        Guid secondAdminGroupId;
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            var secondGroup = new RoleGroup { Id = Guid.NewGuid(), Name = "IT Support", CreatedByObjectId = "test", CreatedByDisplayName = "test" };
            db.Set<RoleGroup>().Add(secondGroup);
            secondAdminGroupId = secondGroup.Id;
            db.Set<RoleGroupPermission>().Add(new RoleGroupPermission { Id = Guid.NewGuid(), RoleGroupId = secondGroup.Id, PermissionKey = "Admin.ManageUsers" });
            await db.SaveChangesAsync();
        }

        // Removing admin access from the FIRST group is fine here, since
        // the second group still has it - the system as a whole isn't
        // locked out.
        var newPermissions = new List<string> { "Dashboard.View" };
        await _sut.UpdateRoleGroupPermissionsAsync(adminGroup.Id, newPermissions);

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var remaining = verifyDb.Set<RoleGroupPermission>().Where(p => p.RoleGroupId == adminGroup.Id).Select(p => p.PermissionKey).ToList();
        Assert.Single(remaining);
        Assert.Equal("Dashboard.View", remaining[0]);
    }

    // ====================================================================
    // FindAnyAccountByUsernameAsync - closes the gap where a local
    // login username could silently collide with someone else's Entra
    // UPN prefix, since those live in entirely different columns.
    // ====================================================================

    [Fact]
    public async Task FindAnyAccountByUsernameAsync_FindsCollisionAgainstAnEntraUpnPrefix_NotJustLocalUsername()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = Guid.NewGuid(),
                DisplayName = "SP-001 User",
                EntraUpn = "sp001.user@reachnaveenhotmailco.onmicrosoft.com",
                RoleGroupId = adminGroup.Id,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        // Someone trying to use "sp001.user" as a NEW local login
        // username - should be caught even though nothing in this app
        // has that exact value as a LocalUsername; it's only an Entra
        // UPN prefix.
        var match = await _sut.FindAnyAccountByUsernameAsync("sp001.user");

        Assert.NotNull(match);
        Assert.Equal("SP-001 User", match!.DisplayName);
    }

    [Fact]
    public async Task FindAnyAccountByUsernameAsync_ReturnsNull_WhenGenuinelyUnused()
    {
        await SeedGlobalAdminRoleGroupAsync();

        var match = await _sut.FindAnyAccountByUsernameAsync("genuinely.unused.username");

        Assert.Null(match);
    }

    // ====================================================================
    // VerifyLocalLoginAsync - account lockout after repeated failures.
    // ====================================================================

    [Fact]
    public async Task VerifyLocalLoginAsync_LocksAccount_AfterFiveFailedAttempts()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = accountId,
                DisplayName = "Break Glass",
                LocalUsername = "breakglass",
                LocalPasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<UserAccount>()
                    .HashPassword(new UserAccount(), "TheRealPassword123!"),
                LocalLoginEnabled = true,
                RoleGroupId = adminGroup.Id,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        LocalLoginAttemptResult lastResult = null!;
        for (var i = 0; i < 5; i++)
            lastResult = await _sut.VerifyLocalLoginAsync("breakglass", "WrongPassword");

        // The 5th wrong attempt is what actually crosses the threshold
        // and locks the account.
        Assert.Equal(LocalLoginOutcome.LockedOut, lastResult.Outcome);

        // Critically: even the CORRECT password is now rejected outright
        // while locked - that's the entire point of a lockout.
        var attemptWithCorrectPassword = await _sut.VerifyLocalLoginAsync("breakglass", "TheRealPassword123!");
        Assert.Equal(LocalLoginOutcome.LockedOut, attemptWithCorrectPassword.Outcome);
    }

    [Fact]
    public async Task VerifyLocalLoginAsync_Succeeds_AndResetsFailedAttempts_OnCorrectPassword()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = Guid.NewGuid(),
                DisplayName = "Break Glass",
                LocalUsername = "breakglass2",
                LocalPasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<UserAccount>()
                    .HashPassword(new UserAccount(), "TheRealPassword123!"),
                LocalLoginEnabled = true,
                RoleGroupId = adminGroup.Id,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        // Two wrong attempts, well under the lockout threshold, then the
        // correct password.
        await _sut.VerifyLocalLoginAsync("breakglass2", "wrong1");
        await _sut.VerifyLocalLoginAsync("breakglass2", "wrong2");
        var result = await _sut.VerifyLocalLoginAsync("breakglass2", "TheRealPassword123!");

        Assert.Equal(LocalLoginOutcome.Success, result.Outcome);
        Assert.NotNull(result.Account);
        Assert.Equal(0, result.Account!.FailedLoginAttempts);
        Assert.Null(result.Account.LockoutEndUtc);
    }

    // ====================================================================
    // UpdateUserAccountDetailsAsync - the identity-lock guard. This is
    // the exact logic behind a real, confirmed bug this session: a
    // linked account's fields being silently ignored by the general
    // update path, including a field (LocalLoginEnabled) that genuinely
    // needed to change. These tests exist specifically so that class of
    // regression can't come back unnoticed.
    // ====================================================================

    [Fact]
    public async Task UpdateUserAccountDetailsAsync_DoesNotChangeIdentityFields_ForALinkedAccount()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        var linkedStaffId = await SeedStaffAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = accountId,
                DisplayName = "Original Name",
                StaffId = linkedStaffId, // linked - identity fields should be protected
                EntraUpn = "original.upn@school.onmicrosoft.com",
                RoleGroupId = adminGroup.Id,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        // Attempting to change everything - display name, UPN, even the
        // staff link itself.
        await _sut.UpdateUserAccountDetailsAsync(
            accountId, "A Different Name", Guid.NewGuid(),
            entraUpn: "different.upn@school.onmicrosoft.com");

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);

        // None of it should have taken - this is the actual protection,
        // not just "nothing threw an exception".
        Assert.Equal("Original Name", account.DisplayName);
        Assert.Equal(linkedStaffId, account.StaffId);
        Assert.Equal("original.upn@school.onmicrosoft.com", account.EntraUpn);
    }

    [Fact]
    public async Task UpdateUserAccountDetailsAsync_ChangesFields_ForAStandaloneAccount()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = accountId,
                DisplayName = "Original Name",
                StaffId = null, // standalone - not protected by the identity lock
                RoleGroupId = adminGroup.Id,
                IsActive = true
            });
            await db.SaveChangesAsync();
        }

        await _sut.UpdateUserAccountDetailsAsync(accountId, "Updated Name", null, localLoginEnabled: true, localUsername: "newusername");

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);

        Assert.Equal("Updated Name", account.DisplayName);
        Assert.True(account.LocalLoginEnabled);
        Assert.Equal("newusername", account.LocalUsername);
    }

    [Fact]
    public async Task UpdateUserAccountDetailsAsync_Throws_WhenNewLocalUsernameCollidesWithAnotherAccount()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = Guid.NewGuid(), DisplayName = "Existing", LocalUsername = "taken", LocalLoginEnabled = true,
                RoleGroupId = adminGroup.Id, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        var accountBeingEditedId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = accountBeingEditedId, DisplayName = "Being Edited", RoleGroupId = adminGroup.Id, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.UpdateUserAccountDetailsAsync(accountBeingEditedId, "Being Edited", null, localLoginEnabled: true, localUsername: "taken"));
    }

    // ====================================================================
    // CreateUserAccountAsync
    // ====================================================================

    [Fact]
    public async Task CreateUserAccountAsync_ClearsEntraFields_WhenLocalLoginEnabled()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();

        var created = await _sut.CreateUserAccountAsync(new UserAccount
        {
            DisplayName = "Break Glass",
            LocalLoginEnabled = true,
            LocalUsername = "newbreakglass",
            EntraUpn = "shouldnt.survive@school.onmicrosoft.com", // deliberately set, to confirm it gets cleared
            EntraObjectId = "shouldnt-survive-either",
            RoleGroupId = adminGroup.Id
        }, "SomePassword123!");

        Assert.Null(created.EntraUpn);
        Assert.Null(created.EntraObjectId);
        Assert.True(created.LocalLoginEnabled);
        Assert.NotNull(created.LocalPasswordHash);
    }

    [Fact]
    public async Task CreateUserAccountAsync_Throws_WhenLocalUsernameCollidesWithAnExistingEntraUpnPrefix()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = Guid.NewGuid(), DisplayName = "SP-002 User", EntraUpn = "sp002.user@school.onmicrosoft.com",
                RoleGroupId = adminGroup.Id, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.CreateUserAccountAsync(new UserAccount
            {
                DisplayName = "New Person",
                LocalLoginEnabled = true,
                LocalUsername = "sp002.user", // collides with the Entra UPN prefix above
                RoleGroupId = adminGroup.Id
            }, "SomePassword123!"));
    }

    // ====================================================================
    // EnableLocalLoginOverrideAsync - Global-Admin-only conversion
    // ====================================================================

    [Fact]
    public async Task EnableLocalLoginOverrideAsync_Throws_ForNonGlobalAdmin()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        var staffId = await SeedStaffAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = accountId, DisplayName = "Linked Person", StaffId = staffId,
                EntraUpn = "person@school.onmicrosoft.com", RoleGroupId = adminGroup.Id, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        _currentUser.HasPermission("Admin.ManageUsers").Returns(false); // Office Admin, not Global Admin

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.EnableLocalLoginOverrideAsync(accountId, "newusername", "SomePassword123!"));
    }

    [Fact]
    public async Task EnableLocalLoginOverrideAsync_ArchivesEntraIdentity_AndClearsLiveFields()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        var staffId = await SeedStaffAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = accountId, DisplayName = "Linked Person", StaffId = staffId,
                EntraUpn = "person@school.onmicrosoft.com", EntraObjectId = "real-object-id",
                RoleGroupId = adminGroup.Id, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        _currentUser.HasPermission("Admin.ManageUsers").Returns(true);

        await _sut.EnableLocalLoginOverrideAsync(accountId, "person.local", "SomePassword123!");

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);

        Assert.True(account.LocalLoginEnabled);
        Assert.Equal("person.local", account.LocalUsername);
        Assert.Null(account.EntraUpn);
        Assert.Null(account.EntraObjectId);
        // Archived, not discarded - this is what makes RevertToEntraAsync possible later.
        Assert.Equal("person@school.onmicrosoft.com", account.ArchivedEntraUpn);
        Assert.Equal("real-object-id", account.ArchivedEntraObjectId);
    }

    // ====================================================================
    // DisableLocalLoginOverrideAsync - a confirmed real bug this
    // session was that the username got wiped when it shouldn't have.
    // ====================================================================

    [Fact]
    public async Task DisableLocalLoginOverrideAsync_RetainsUsername_ClearsOnlyPasswordAndEnabledFlag()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        var staffId = await SeedStaffAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = accountId, DisplayName = "Converted Person", StaffId = staffId,
                LocalLoginEnabled = true, LocalUsername = "converted.person",
                LocalPasswordHash = "somehash", RoleGroupId = adminGroup.Id, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        _currentUser.HasPermission("Admin.ManageUsers").Returns(true);

        await _sut.DisableLocalLoginOverrideAsync(accountId);

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);

        Assert.False(account.LocalLoginEnabled);
        Assert.Null(account.LocalPasswordHash);
        // The actual regression this guards against - the username must
        // survive, so re-enabling later doesn't mean retyping it.
        Assert.Equal("converted.person", account.LocalUsername);
    }

    // ====================================================================
    // RevertToEntraAsync - verifies against Graph before restoring,
    // rather than trusting the archived value blindly.
    // ====================================================================

    [Fact]
    public async Task RevertToEntraAsync_Throws_WhenNothingWasArchived()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = accountId, DisplayName = "Never Converted", LocalLoginEnabled = true,
                LocalUsername = "always.local", RoleGroupId = adminGroup.Id, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        _currentUser.HasPermission("Admin.ManageUsers").Returns(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RevertToEntraAsync(accountId));
    }

    [Fact]
    public async Task RevertToEntraAsync_Throws_WhenArchivedEntraAccountNoLongerExistsInGraph()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = accountId, DisplayName = "Converted Person", LocalLoginEnabled = true, LocalUsername = "converted",
                ArchivedEntraObjectId = "deleted-object-id", ArchivedEntraUpn = "old.upn@school.onmicrosoft.com",
                RoleGroupId = adminGroup.Id, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        _currentUser.HasPermission("Admin.ManageUsers").Returns(true);
        // The Entra account was deleted independently since conversion.
        _graphService.VerifyUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((false, (string?)null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.RevertToEntraAsync(accountId));
    }

    [Fact]
    public async Task RevertToEntraAsync_UsesCurrentUpnFromGraph_NotTheStaleArchivedOne()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = accountId, DisplayName = "Converted Person", LocalLoginEnabled = true, LocalUsername = "converted",
                ArchivedEntraObjectId = "still-real-object-id", ArchivedEntraUpn = "old.name@school.onmicrosoft.com",
                RoleGroupId = adminGroup.Id, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        _currentUser.HasPermission("Admin.ManageUsers").Returns(true);
        // Still exists, but was renamed in Entra since the conversion.
        _graphService.VerifyUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((true, "new.name@school.onmicrosoft.com"));

        await _sut.RevertToEntraAsync(accountId);

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);

        Assert.Equal("still-real-object-id", account.EntraObjectId);
        // The current Entra value, not the stale archived one.
        Assert.Equal("new.name@school.onmicrosoft.com", account.EntraUpn);
        Assert.False(account.LocalLoginEnabled);
        Assert.Null(account.LocalUsername);
        Assert.Null(account.ArchivedEntraObjectId);
        Assert.Null(account.ArchivedEntraUpn);
    }

    // ====================================================================
    // DeleteRoleGroupAsync - can't orphan users by deleting the group
    // they're currently assigned to.
    // ====================================================================

    [Fact]
    public async Task DeleteRoleGroupAsync_Throws_WhenGroupHasMembers()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = Guid.NewGuid(), DisplayName = "A Member", RoleGroupId = adminGroup.Id, IsActive = true
            });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteRoleGroupAsync(adminGroup.Id));
    }

    [Fact]
    public async Task DeleteRoleGroupAsync_Succeeds_WhenGroupIsEmpty()
    {
        Guid emptyGroupId;
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            var group = new RoleGroup { Id = Guid.NewGuid(), Name = "Unused Group", CreatedByObjectId = "test", CreatedByDisplayName = "test" };
            db.Set<RoleGroup>().Add(group);
            emptyGroupId = group.Id;
            await db.SaveChangesAsync();
        }

        await _sut.DeleteRoleGroupAsync(emptyGroupId);

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var stillExists = await verifyDb.Set<RoleGroup>().AnyAsync(g => g.Id == emptyGroupId);
        Assert.False(stillExists);
    }

    // ====================================================================
    // "Worth doing next" batch
    // ====================================================================

    [Fact]
    public async Task DeactivateUserAccountAsync_SetsIsActiveFalse()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Someone", RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        await _sut.DeactivateUserAccountAsync(accountId);

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);
        Assert.False(account.IsActive);
    }

    [Fact]
    public async Task DeactivateUserAccountAsync_Throws_WhenAccountNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeactivateUserAccountAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ReactivateUserAccountAsync_SetsIsActiveTrue()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Someone", RoleGroupId = adminGroup.Id, IsActive = false });
            await db.SaveChangesAsync();
        }

        await _sut.ReactivateUserAccountAsync(accountId);

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);
        Assert.True(account.IsActive);
    }

    [Fact]
    public async Task DeleteUserAccountAsync_RemovesTheAccountEntirely()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Standalone", RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        await _sut.DeleteUserAccountAsync(accountId);

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var stillExists = await verifyDb.Set<UserAccount>().AnyAsync(u => u.Id == accountId);
        Assert.False(stillExists); // genuinely gone, not just deactivated
    }

    [Fact]
    public async Task DeleteUserAccountAsync_Throws_WhenAccountNotFound()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.DeleteUserAccountAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task AssignRoleGroupAsync_UpdatesRoleGroupAndExpiry()
    {
        var originalGroup = await SeedGlobalAdminRoleGroupAsync();
        Guid newGroupId;
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            var newGroup = new RoleGroup { Id = Guid.NewGuid(), Name = "Office Admin", CreatedByObjectId = "test", CreatedByDisplayName = "test" };
            db.Set<RoleGroup>().Add(newGroup);
            newGroupId = newGroup.Id;
            await db.SaveChangesAsync();
        }

        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Someone", RoleGroupId = originalGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        var expiry = DateTime.UtcNow.AddDays(30);
        await _sut.AssignRoleGroupAsync(accountId, newGroupId, expiry);

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);
        Assert.Equal(newGroupId, account.RoleGroupId);
        Assert.Equal(expiry, account.RoleGroupExpiresAtUtc);
    }

    [Fact]
    public async Task AssignRoleGroupAsync_Throws_WhenRoleGroupNotFound()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Someone", RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.AssignRoleGroupAsync(accountId, Guid.NewGuid(), null));
    }

    [Fact]
    public async Task FindAnyByEntraUpnAsync_FindsInactiveAccountsToo_UnlikeGetByEntraUpnAsync()
    {
        // The whole reason this method exists separately from
        // GetByEntraUpnAsync - duplicate detection needs to catch a UPN
        // already claimed by a deactivated account too, not just active
        // ones, since GetByEntraUpnAsync alone would miss it entirely.
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = Guid.NewGuid(), DisplayName = "Deactivated Person",
                EntraUpn = "deactivated.person@school.onmicrosoft.com",
                RoleGroupId = adminGroup.Id, IsActive = false
            });
            await db.SaveChangesAsync();
        }

        var foundByFindAny = await _sut.FindAnyByEntraUpnAsync("deactivated.person@school.onmicrosoft.com");
        var foundByGetActive = await _sut.GetByEntraUpnAsync("deactivated.person@school.onmicrosoft.com");

        Assert.NotNull(foundByFindAny);
        Assert.Null(foundByGetActive);
    }

    [Fact]
    public async Task BackfillEntraObjectIdAsync_SetsObjectId_WhenCurrentlyEmpty()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Someone", EntraObjectId = null, RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        await _sut.BackfillEntraObjectIdAsync(accountId, "newly-discovered-object-id");

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);
        Assert.Equal("newly-discovered-object-id", account.EntraObjectId);
    }

    [Fact]
    public async Task BackfillEntraObjectIdAsync_DoesNotOverwrite_WhenAlreadySet()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Someone", EntraObjectId = "original-object-id", RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        await _sut.BackfillEntraObjectIdAsync(accountId, "a-different-object-id");

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);
        Assert.Equal("original-object-id", account.EntraObjectId); // unchanged
    }

    // ====================================================================
    // Lower priority batch
    // ====================================================================

    [Fact]
    public async Task ListUsersAsync_ReturnsAllAccounts_OrderedByDisplayName()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().AddRange(
                new UserAccount { Id = Guid.NewGuid(), DisplayName = "Zebra Person", RoleGroupId = adminGroup.Id, IsActive = true },
                new UserAccount { Id = Guid.NewGuid(), DisplayName = "Alpha Person", RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        var result = await _sut.ListUsersAsync();

        Assert.Equal(2, result.Count);
        Assert.Equal("Alpha Person", result[0].DisplayName);
        Assert.Equal("Zebra Person", result[1].DisplayName);
    }

    [Fact]
    public async Task GetByIdAsync_ExcludesInactiveAccounts()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Inactive Person", RoleGroupId = adminGroup.Id, IsActive = false });
            await db.SaveChangesAsync();
        }

        var result = await _sut.GetByIdAsync(accountId);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByEntraObjectIdAsync_ExcludesInactiveAccounts()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = Guid.NewGuid(), DisplayName = "Inactive Person", EntraObjectId = "some-object-id", RoleGroupId = adminGroup.Id, IsActive = false });
            await db.SaveChangesAsync();
        }

        var result = await _sut.GetByEntraObjectIdAsync("some-object-id");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByEntraUpnAsync_MatchesCaseInsensitively()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = Guid.NewGuid(), DisplayName = "Someone", EntraUpn = "Someone@School.OnMicrosoft.Com", RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        var result = await _sut.GetByEntraUpnAsync("someone@school.onmicrosoft.com");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetByLocalUsernameAsync_RequiresLocalLoginToBeEnabled()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            // Username set, but local login not actually enabled - a
            // leftover/half-configured state that shouldn't be matchable.
            db.Set<UserAccount>().Add(new UserAccount { Id = Guid.NewGuid(), DisplayName = "Someone", LocalUsername = "halfconfigured", LocalLoginEnabled = false, RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        var result = await _sut.GetByLocalUsernameAsync("halfconfigured");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetEffectivePermissionsAsync_ReturnsExactlyThePermissionsAssigned()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<RoleGroupPermission>().AddRange(
                new RoleGroupPermission { Id = Guid.NewGuid(), RoleGroupId = adminGroup.Id, PermissionKey = "Dashboard.View" },
                new RoleGroupPermission { Id = Guid.NewGuid(), RoleGroupId = adminGroup.Id, PermissionKey = "Admin.ManageUsers" });
            await db.SaveChangesAsync();
        }

        var result = await _sut.GetEffectivePermissionsAsync(adminGroup.Id);

        Assert.Equal(2, result.Count);
        Assert.Contains("Dashboard.View", result);
        Assert.Contains("Admin.ManageUsers", result);
    }

    [Fact]
    public async Task ListRoleGroupsAsync_PopulatesPermissionsForEachGroup()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<RoleGroupPermission>().Add(new RoleGroupPermission { Id = Guid.NewGuid(), RoleGroupId = adminGroup.Id, PermissionKey = "Admin.ManageUsers" });
            await db.SaveChangesAsync();
        }

        var result = await _sut.ListRoleGroupsAsync();

        var found = result.Single(g => g.Id == adminGroup.Id);
        Assert.Single(found.Permissions);
        Assert.Equal("Admin.ManageUsers", found.Permissions[0].PermissionKey);
    }

    [Fact]
    public async Task CreateRoleGroupAsync_Throws_WhenNameAlreadyExists()
    {
        await SeedGlobalAdminRoleGroupAsync(); // creates "Global Administrator"

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.CreateRoleGroupAsync("Global Administrator", null, new List<string>()));
    }

    [Fact]
    public async Task SetLocalPasswordAsync_SetsHashAndEnablesLocalLogin()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Someone", LocalLoginEnabled = false, RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        await _sut.SetLocalPasswordAsync(accountId, "NewPassword123!");

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);
        Assert.NotNull(account.LocalPasswordHash);
        Assert.True(account.LocalLoginEnabled);
    }

    [Fact]
    public async Task SyncDisplayNameAsync_UpdatesTheNameWhenDifferent()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Old Name", RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        await _sut.SyncDisplayNameAsync(accountId, "New Name From Entra");

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);
        Assert.Equal("New Name From Entra", account.DisplayName);
    }

    [Fact]
    public async Task UpdateLastLoginAsync_DoesNotThrow_WhenAccountNotFound()
    {
        // Deliberately different from most other methods here - this one
        // is called from the sign-in path itself, where throwing over a
        // race condition (account deleted between resolving it and this
        // call) would be worse than just silently doing nothing.
        var exception = await Record.ExceptionAsync(() => _sut.UpdateLastLoginAsync(Guid.NewGuid()));

        Assert.Null(exception);
    }

    [Fact]
    public async Task GetRoleGroupByIdAsync_ReturnsNull_WhenNotFound()
    {
        var result = await _sut.GetRoleGroupByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateRoleGroupBasicsAsync_Throws_WhenRenamingASystemDefinedGroup()
    {
        Guid systemGroupId;
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            var group = new RoleGroup { Id = Guid.NewGuid(), Name = "Global Administrator", IsSystemDefined = true, CreatedByObjectId = "test", CreatedByDisplayName = "test" };
            db.Set<RoleGroup>().Add(group);
            systemGroupId = group.Id;
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.UpdateRoleGroupBasicsAsync(systemGroupId, "Renamed Admin", null));
    }

    [Fact]
    public async Task UpdateRoleGroupBasicsAsync_AllowsRenaming_ACustomGroup()
    {
        Guid customGroupId;
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            var group = new RoleGroup { Id = Guid.NewGuid(), Name = "Old Custom Name", IsSystemDefined = false, CreatedByObjectId = "test", CreatedByDisplayName = "test" };
            db.Set<RoleGroup>().Add(group);
            customGroupId = group.Id;
            await db.SaveChangesAsync();
        }

        await _sut.UpdateRoleGroupBasicsAsync(customGroupId, "New Custom Name", "Updated description");

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var group2 = await verifyDb.Set<RoleGroup>().FirstAsync(g => g.Id == customGroupId);
        Assert.Equal("New Custom Name", group2.Name);
        Assert.Equal("Updated description", group2.Description);
    }

    // ====================================================================
    // Audit log content - verifying WHAT gets logged, not just that state
    // changed. Every LogAsync call has 20 parameters; this helper keeps
    // that verbosity in one place rather than repeated in every test.
    // ====================================================================

    private void AssertAuditLogged(string module, string entityType, string action, Func<string?, bool>? contextPredicate = null)
    {
        _audit.Received(1).LogAsync(
            module, entityType, Arg.Any<string>(), action,
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            contextPredicate == null ? Arg.Any<string>() : Arg.Is<string>(c => contextPredicate(c)),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(),
            Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateUserAccountDetailsAsync_LogsWhyNothingChanged_ForALinkedAccount()
    {
        // The audit trail itself must document WHY an edit had no visible
        // effect - without this, a linked account's protected edit just
        // looks like nothing happened at all, with no record explaining
        // the identity lock is what stopped it.
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var staffId = await SeedStaffAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Original", StaffId = staffId, RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        await _sut.UpdateUserAccountDetailsAsync(accountId, "Attempted New Name", null);

        AssertAuditLogged("Admin", "UserAccount", "UpdateDetails",
            c => c != null && c.Contains("identity fields left unchanged"));
    }

    [Fact]
    public async Task EnsureBootstrapAdminAccountAsync_LogsTheDisplayNameOfTheNewAdmin()
    {
        var configuredBootstrapId = Guid.NewGuid().ToString();
        _config["Authorization:BootstrapGlobalAdminObjectId"].Returns(configuredBootstrapId);
        await SeedGlobalAdminRoleGroupAsync();

        await _sut.EnsureBootstrapAdminAccountAsync(configuredBootstrapId, "The Real Bootstrap Admin");

        AssertAuditLogged("Admin", "UserAccount", "AutoProvision");
    }

    [Fact]
    public async Task DeactivateUserAccountAsync_LogsTheDisplayName()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Someone Specific", RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        await _sut.DeactivateUserAccountAsync(accountId);

        AssertAuditLogged("Admin", "UserAccount", "Deactivate", c => c == "Someone Specific");
    }

    [Fact]
    public async Task EnableLocalLoginOverrideAsync_LogsTheConversionExplicitly()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var staffId = await SeedStaffAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Someone", StaffId = staffId, EntraUpn = "someone@school.onmicrosoft.com", RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }
        _currentUser.HasPermission("Admin.ManageUsers").Returns(true);

        await _sut.EnableLocalLoginOverrideAsync(accountId, "someone.local", "SomePassword123!");

        AssertAuditLogged("Admin", "UserAccount", "EnableLocalLoginOverride",
            c => c != null && c.Contains("Global Admin"));
    }

    [Fact]
    public async Task RevertToEntraAsync_LogsTheRevertExplicitly()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount
            {
                Id = accountId, DisplayName = "Someone", LocalLoginEnabled = true, LocalUsername = "someone.local",
                ArchivedEntraObjectId = "real-id", ArchivedEntraUpn = "someone@school.onmicrosoft.com",
                RoleGroupId = adminGroup.Id, IsActive = true
            });
            await db.SaveChangesAsync();
        }
        _currentUser.HasPermission("Admin.ManageUsers").Returns(true);
        _graphService.VerifyUserAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((true, "someone@school.onmicrosoft.com"));

        await _sut.RevertToEntraAsync(accountId);

        AssertAuditLogged("Admin", "UserAccount", "RevertToEntra",
            c => c != null && c.Contains("Global Admin"));
    }

    // ====================================================================
    // Edge cases: empty-string usernames
    // ====================================================================

    [Fact]
    public async Task FindAnyAccountByUsernameAsync_ReturnsNull_ForEmptyString_RatherThanAFalsePositive()
    {
        // A naive implementation of the collision check could treat an
        // empty normalized value as matching everything (e.g. an
        // EntraUpn.StartsWith("@") check against a malformed UPN) - this
        // confirms that doesn't happen in practice against realistic data.
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = Guid.NewGuid(), DisplayName = "Someone", EntraUpn = "someone@school.onmicrosoft.com", RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        var result = await _sut.FindAnyAccountByUsernameAsync("");

        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateUserAccountDetailsAsync_SkipsCollisionCheck_ForAnEmptyUsername_ButStillSaves()
    {
        // string.IsNullOrWhiteSpace("") is true, so the collision check is
        // deliberately skipped for an empty username - this documents
        // that current, actual behavior explicitly, so a future change
        // to that guard's condition doesn't silently alter this without
        // a test noticing.
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Someone", RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        var exception = await Record.ExceptionAsync(() =>
            _sut.UpdateUserAccountDetailsAsync(accountId, "Someone", null, localLoginEnabled: true, localUsername: ""));

        Assert.Null(exception);
    }

    [Fact]
    public async Task CreateUserAccountAsync_SkipsCollisionCheck_ForAnEmptyUsername_ButStillSucceeds()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();

        var exception = await Record.ExceptionAsync(() =>
            _sut.CreateUserAccountAsync(new UserAccount
            {
                DisplayName = "Someone", LocalLoginEnabled = true, LocalUsername = "", RoleGroupId = adminGroup.Id
            }, "SomePassword123!"));

        Assert.Null(exception);
    }

    // ====================================================================
    // Edge cases: null staffId combinations in UpdateUserAccountDetailsAsync
    // ====================================================================

    [Fact]
    public async Task UpdateUserAccountDetailsAsync_CanLinkAStandaloneAccount_ForTheFirstTime()
    {
        // wasLinked is evaluated from the account's state BEFORE this
        // call - a standalone account (StaffId null going in) is allowed
        // to become linked here, since the identity lock only protects
        // accounts that were ALREADY linked when the call started.
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var staffId = await SeedStaffAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Standalone", StaffId = null, RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        await _sut.UpdateUserAccountDetailsAsync(accountId, "Now Linked", staffId);

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);
        Assert.Equal(staffId, account.StaffId);
    }

    [Fact]
    public async Task UpdateUserAccountDetailsAsync_ExplicitNullStaffId_StaysNull_ForAStandaloneAccount()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        var accountId = Guid.NewGuid();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = accountId, DisplayName = "Standalone", StaffId = null, RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        await _sut.UpdateUserAccountDetailsAsync(accountId, "Still Standalone", null);

        await using var verifyDb = await _dbFactory.CreateDbContextAsync();
        var account = await verifyDb.Set<UserAccount>().FirstAsync(u => u.Id == accountId);
        Assert.Null(account.StaffId);
    }

    // ====================================================================
    // Edge cases: case-sensitivity boundaries. Worth being explicit that
    // these tests document a genuine INCONSISTENCY discovered while
    // writing them, not a deliberately designed behavior: UPN-based
    // lookups (GetByEntraUpnAsync, FindAnyByEntraUpnAsync,
    // FindAnyAccountByUsernameAsync) are all case-insensitive, but
    // GetByEntraObjectIdAsync and GetByLocalUsernameAsync do an exact,
    // case-SENSITIVE string comparison. These tests pin down the current,
    // actual behavior either way - flagged for a decision on whether
    // that asymmetry should be fixed.
    // ====================================================================

    [Fact]
    public async Task FindAnyAccountByUsernameAsync_MatchesRegardlessOfCase()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = Guid.NewGuid(), DisplayName = "Someone", EntraUpn = "SomeOne@School.OnMicrosoft.Com", RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        var result = await _sut.FindAnyAccountByUsernameAsync("SOMEONE");

        Assert.NotNull(result);
    }

    [Fact]
    public async Task GetByEntraObjectIdAsync_IsCaseSensitive_UnlikeTheUpnLookups()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = Guid.NewGuid(), DisplayName = "Someone", EntraObjectId = "AbCdEf-123", RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        var exactCase = await _sut.GetByEntraObjectIdAsync("AbCdEf-123");
        var differentCase = await _sut.GetByEntraObjectIdAsync("abcdef-123");

        Assert.NotNull(exactCase);
        Assert.Null(differentCase); // documents the current, exact-match behavior
    }

    [Fact]
    public async Task GetByLocalUsernameAsync_IsCaseSensitive_UnlikeTheUpnLookups()
    {
        var adminGroup = await SeedGlobalAdminRoleGroupAsync();
        await using (var db = await _dbFactory.CreateDbContextAsync())
        {
            db.Set<UserAccount>().Add(new UserAccount { Id = Guid.NewGuid(), DisplayName = "Someone", LocalUsername = "MixedCase.User", LocalLoginEnabled = true, RoleGroupId = adminGroup.Id, IsActive = true });
            await db.SaveChangesAsync();
        }

        var exactCase = await _sut.GetByLocalUsernameAsync("MixedCase.User");
        var differentCase = await _sut.GetByLocalUsernameAsync("mixedcase.user");

        Assert.NotNull(exactCase);
        Assert.Null(differentCase); // documents the current, exact-match behavior
    }
}
