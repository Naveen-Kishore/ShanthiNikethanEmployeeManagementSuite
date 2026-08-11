-- =============================================================================
-- Check for, and optionally clean up, a stray account created by the
-- placeholder-text bootstrap bug (fixed in Program.cs) - the app was
-- treating "PASTE_YOUR_PRODUCTION_TENANT_OBJECT_ID_HERE" as if it were a
-- real Entra Object ID, creating an orphaned "Bootstrap Administrator"
-- account nobody could ever actually sign into.
-- =============================================================================
USE ShanthiNikethanEmployeeManagement_DEV;  -- run again against _Prod with this line changed
GO

-- Step 1: just look first - confirm whether this actually happened here
-- before deleting anything.
SELECT Id, DisplayName, EntraObjectId, RoleGroupId, CreatedAtUtc, CreatedByDisplayName
FROM dbo.UserAccount
WHERE EntraObjectId = 'PASTE_YOUR_PRODUCTION_TENANT_OBJECT_ID_HERE';

-- Step 2: if the above returned a row, uncomment and run this to remove it.
-- Left commented deliberately - review the SELECT result first, don't run
-- a DELETE blind.

-- DELETE FROM dbo.UserAccount
-- WHERE EntraObjectId = 'PASTE_YOUR_PRODUCTION_TENANT_OBJECT_ID_HERE';
GO
