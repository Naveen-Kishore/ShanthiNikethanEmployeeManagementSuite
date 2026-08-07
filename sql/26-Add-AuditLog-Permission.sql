-- =============================================================================
-- Add Admin.ViewAuditLog permission, grant it to Global Administrator
-- =============================================================================
-- Only Global Administrator gets this automatically. Grant it to any other
-- role group (Correspondent, Principal, etc.) manually via Admin Console ->
-- Role Groups if you want them to see the audit log too - same as any
-- other permission, no special handling needed there.
--
-- Safe to re-run: the NOT EXISTS guard means running this twice won't
-- create a duplicate row.
-- =============================================================================
USE ShanthiNikethanEmployeeManagement_DEV;  -- run again against _Prod with this line changed
GO

DECLARE @GlobalAdminId UNIQUEIDENTIFIER = (SELECT Id FROM dbo.RoleGroup WHERE Name = 'Global Administrator');

IF @GlobalAdminId IS NULL
BEGIN
    PRINT 'ERROR: Global Administrator role group not found - run the Admin Console foundation script first.';
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM dbo.RoleGroupPermission
    WHERE RoleGroupId = @GlobalAdminId AND PermissionKey = 'Admin.ViewAuditLog'
)
BEGIN
    INSERT INTO dbo.RoleGroupPermission (Id, RoleGroupId, PermissionKey)
    VALUES (NEWID(), @GlobalAdminId, 'Admin.ViewAuditLog');
    PRINT 'Granted Admin.ViewAuditLog to Global Administrator.';
END
ELSE
BEGIN
    PRINT 'Admin.ViewAuditLog already granted to Global Administrator - nothing to do.';
END
GO
