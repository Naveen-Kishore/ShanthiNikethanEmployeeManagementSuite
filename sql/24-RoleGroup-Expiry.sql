-- =============================================================================
-- Shanthi Nikethan Employee Management — Role group assignment expiry
-- =============================================================================
USE ShanthiNikethanEmployeeManagement_Dev;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.UserAccount') AND name = 'RoleGroupExpiresAtUtc')
BEGIN
    ALTER TABLE dbo.UserAccount ADD RoleGroupExpiresAtUtc DATETIME2 NULL;
END
GO

PRINT 'RoleGroupExpiresAtUtc column added.';
