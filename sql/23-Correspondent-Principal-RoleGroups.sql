-- =============================================================================
-- Shanthi Nikethan Employee Management — Correspondent & Principal role groups
-- =============================================================================
-- Correspondent: everything Global Administrator has, except the two
-- Admin.* permissions (no access to Administration module).
--
-- Principal: exactly Office Admin's permission set, plus
-- Attendance.AdminOverride (the "correct past-day attendance" capability).
-- Strictly less than Correspondent — no Payroll, no
-- StaffDirectory.ViewFinancials, no Dashboard.ViewFinancials anywhere.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement_Dev;
GO

DECLARE @CorrespondentId UNIQUEIDENTIFIER = NEWID();
DECLARE @PrincipalId UNIQUEIDENTIFIER = NEWID();

IF NOT EXISTS (SELECT 1 FROM dbo.RoleGroup WHERE Name = 'Correspondent')
BEGIN
    INSERT INTO dbo.RoleGroup (Id, Name, Description, IsSystemDefined, CreatedAtUtc)
    VALUES (@CorrespondentId, 'Correspondent', 'Full access to every module except Administration.', 0, SYSUTCDATETIME());

    INSERT INTO dbo.RoleGroupPermission (RoleGroupId, PermissionKey)
    SELECT @CorrespondentId, PermissionKey FROM (VALUES
        ('Dashboard.View'), ('Dashboard.ViewFinancials'),
        ('StaffDirectory.View'), ('StaffDirectory.ViewFinancials'), ('StaffDirectory.Edit'),
        ('Payroll.View'), ('Payroll.Manage'),
        ('Leave.View'), ('Leave.Manage'),
        ('Attendance.View'), ('Attendance.Mark'), ('Attendance.AdminOverride')
    ) AS p(PermissionKey);
END
ELSE
BEGIN
    SET @CorrespondentId = (SELECT Id FROM dbo.RoleGroup WHERE Name = 'Correspondent');
END

IF NOT EXISTS (SELECT 1 FROM dbo.RoleGroup WHERE Name = 'Principal')
BEGIN
    INSERT INTO dbo.RoleGroup (Id, Name, Description, IsSystemDefined, CreatedAtUtc)
    VALUES (@PrincipalId, 'Principal', 'Office Admin access plus attendance correction. No financial data anywhere.', 0, SYSUTCDATETIME());

    INSERT INTO dbo.RoleGroupPermission (RoleGroupId, PermissionKey)
    SELECT @PrincipalId, PermissionKey FROM (VALUES
        ('Dashboard.View'),
        ('StaffDirectory.View'), ('StaffDirectory.Edit'),
        ('Leave.View'), ('Leave.Manage'),
        ('Attendance.View'), ('Attendance.Mark'), ('Attendance.AdminOverride')
    ) AS p(PermissionKey);
END

PRINT 'Correspondent and Principal role groups created.';
