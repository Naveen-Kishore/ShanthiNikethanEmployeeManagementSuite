-- =============================================================================
-- Adds ArchivedEntraObjectId/ArchivedEntraUpn to UserAccount - preserves a
-- linked account's Entra identity when a Global Admin converts it to local
-- login, so reverting later doesn't mean re-establishing the link from
-- scratch. Populated by EnableLocalLoginOverrideAsync, consumed (with a
-- live Entra verification check) by RevertToEntraAsync.
-- =============================================================================
USE ShanthiNikethanEmployeeManagement_DEV;  -- run again against _Prod with this line changed
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.UserAccount') AND name = 'ArchivedEntraObjectId')
BEGIN
    ALTER TABLE dbo.UserAccount ADD ArchivedEntraObjectId NVARCHAR(100) NULL;
    PRINT 'Added ArchivedEntraObjectId to dbo.UserAccount.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.UserAccount') AND name = 'ArchivedEntraUpn')
BEGIN
    ALTER TABLE dbo.UserAccount ADD ArchivedEntraUpn NVARCHAR(200) NULL;
    PRINT 'Added ArchivedEntraUpn to dbo.UserAccount.';
END
GO
