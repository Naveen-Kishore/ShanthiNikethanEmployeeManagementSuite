-- =============================================================================
-- Creates the ONE local-login account every E2E test signs in as, plus a
-- role group with full access (reasonable here specifically because this
-- is an isolated, test-only database, never production or dev).
--
-- BEFORE RUNNING: run HashGenerator first, then paste its output over
-- PASTE_YOUR_HASH_HERE on the DECLARE line below - there is only ONE
-- place that actually needs editing.
--
-- The validation check deliberately does NOT compare against the literal
-- placeholder text - an earlier version of this script did, and a plain
-- find-and-replace of PASTE_YOUR_HASH_HERE silently corrupted that check
-- too, since the placeholder text appeared in both places. Checking the
-- LENGTH instead (a real hash is 84 characters; the placeholder text is
-- 20) can't be broken the same way, since it never mentions the
-- placeholder text at all.
-- =============================================================================
USE ShanthiNikethanEmployeeManagement_E2E;
GO

SET XACT_ABORT ON;

DECLARE @PasswordHash NVARCHAR(500) = N'PASTE_YOUR_HASH_HERE';

IF LEN(@PasswordHash) < 60
BEGIN
    RAISERROR('The password hash still looks like the placeholder (too short to be a real hash). Run HashGenerator first and paste its full output over PASTE_YOUR_HASH_HERE on the DECLARE line above.', 16, 1);
    RETURN;
END

DECLARE @RoleGroupId UNIQUEIDENTIFIER;

IF NOT EXISTS (SELECT 1 FROM dbo.RoleGroup WHERE Name = 'E2E Test Admin')
BEGIN
    SET @RoleGroupId = NEWID();
    INSERT INTO dbo.RoleGroup (Id, Name, Description, IsSystemDefined, CreatedAtUtc, CreatedByObjectId, CreatedByDisplayName)
    VALUES (@RoleGroupId, 'E2E Test Admin', 'Full access, for automated E2E tests only - never used outside this isolated test database.', 0, SYSUTCDATETIME(), 'e2e-seed-script', 'E2E Seed Script');

    -- Every real permission key, from PermissionCatalog.cs directly -
    -- not a guess, extracted from the actual source file.
    INSERT INTO dbo.RoleGroupPermission (Id, RoleGroupId, PermissionKey)
    SELECT NEWID(), @RoleGroupId, PermissionKey FROM (VALUES
        ('Dashboard.View'), ('Dashboard.ViewFinancials'),
        ('StaffDirectory.View'), ('StaffDirectory.ViewFinancials'), ('StaffDirectory.Edit'),
        ('Payroll.View'), ('Payroll.Manage'), ('Payroll.ViewOwnPayslip'),
        ('Leave.View'), ('Leave.Manage'), ('Leave.ViewOwn'),
        ('Attendance.View'), ('Attendance.Mark'), ('Attendance.AdminOverride'), ('Attendance.ViewOwn'),
        ('Admin.ManageUsers'), ('Admin.ManageRoleGroups'), ('Admin.ViewAuditLog'), ('Admin.ManageAutomationRules')
    ) AS Permissions(PermissionKey);

    PRINT 'Created role group: E2E Test Admin, with all 19 permissions.';
END
ELSE
BEGIN
    SELECT @RoleGroupId = Id FROM dbo.RoleGroup WHERE Name = 'E2E Test Admin';
    PRINT 'E2E Test Admin role group already exists - reusing it.';
END

IF NOT EXISTS (SELECT 1 FROM dbo.UserAccount WHERE LocalUsername = 'e2e.testuser')
BEGIN
    INSERT INTO dbo.UserAccount (Id, DisplayName, LocalUsername, LocalPasswordHash, LocalLoginEnabled,
                                  IsActive, RoleGroupId, FailedLoginAttempts, CreatedAtUtc,
                                  CreatedByObjectId, CreatedByDisplayName)
    VALUES (NEWID(), 'E2E Test User', 'e2e.testuser', @PasswordHash, 1,
            1, @RoleGroupId, 0, SYSUTCDATETIME(),
            'e2e-seed-script', 'E2E Seed Script');

    PRINT 'Created local-login account: e2e.testuser';
END
ELSE
BEGIN
    PRINT 'e2e.testuser already exists - not creating a duplicate. If you need to reset its password, run the UPDATE statement further down instead.';
END
GO

-- If the account already exists and you need to change its password
-- later (e.g. after a lockout during testing), run just this instead of
-- the whole script:
--
-- UPDATE dbo.UserAccount
-- SET LocalPasswordHash = N'PASTE_A_NEW_HASH_HERE', FailedLoginAttempts = 0, LockoutEndUtc = NULL
-- WHERE LocalUsername = 'e2e.testuser';
