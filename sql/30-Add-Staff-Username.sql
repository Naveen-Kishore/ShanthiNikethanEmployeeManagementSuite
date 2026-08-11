-- =============================================================================
-- Adds Username to Staff - was previously only a transient Add Staff form
-- field used to auto-fill the UPN, never actually persisted. Now a real,
-- viewable/editable attribute of the staff profile itself.
-- =============================================================================
USE ShanthiNikethanEmployeeManagement_DEV;  -- run again against _Prod with this line changed
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.Staff') AND name = 'Username')
BEGIN
    ALTER TABLE dbo.Staff ADD Username NVARCHAR(100) NULL;
    PRINT 'Added Username to dbo.Staff.';
END
GO

-- Enforced at the database level too, not just in the app's own live
-- validation - the filtered index only applies to non-null values, so
-- staff created before this column existed (or anyone left blank) don't
-- collide with each other on NULL.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID('dbo.Staff') AND name = 'IX_Staff_Username')
BEGIN
    CREATE UNIQUE INDEX IX_Staff_Username ON dbo.Staff (Username) WHERE Username IS NOT NULL;
    PRINT 'Created unique index IX_Staff_Username.';
END
GO
