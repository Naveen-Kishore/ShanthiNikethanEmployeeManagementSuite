-- Removes the existing (Regular Staff) account so the next app startup
-- finds no account for this Object ID — simulating a genuinely fresh
-- database, so we can actually verify the startup bootstrap fires.
USE ShanthiNikethanEmployeeManagement_DEV;
GO

DELETE FROM dbo.UserAccount
WHERE EntraObjectId = '32264dc0-faa9-4614-917f-e11e7d3fd577';  -- confirm this is your real test-tenant Object ID first

-- Confirm it's gone:
SELECT * FROM dbo.UserAccount WHERE EntraObjectId = '32264dc0-faa9-4614-917f-e11e7d3fd577';
-- Should return zero rows.
GO
