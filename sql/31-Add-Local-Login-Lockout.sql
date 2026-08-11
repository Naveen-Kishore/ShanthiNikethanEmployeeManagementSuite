-- =============================================================================
-- Adds brute-force protection for local login: after repeated failed
-- attempts against a specific account, that account is locked out
-- temporarily, regardless of whether the next attempt would have had the
-- correct password.
-- =============================================================================
USE ShanthiNikethanEmployeeManagement_DEV;  -- run again against _Prod with this line changed
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.UserAccount') AND name = 'FailedLoginAttempts')
BEGIN
    ALTER TABLE dbo.UserAccount ADD FailedLoginAttempts INT NOT NULL DEFAULT 0;
    PRINT 'Added FailedLoginAttempts to dbo.UserAccount.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.UserAccount') AND name = 'LockoutEndUtc')
BEGIN
    ALTER TABLE dbo.UserAccount ADD LockoutEndUtc DATETIME2 NULL;
    PRINT 'Added LockoutEndUtc to dbo.UserAccount.';
END
GO
