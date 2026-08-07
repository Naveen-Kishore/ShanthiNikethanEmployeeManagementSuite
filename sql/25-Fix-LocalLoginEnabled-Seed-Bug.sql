-- =============================================================================
-- Shanthi Nikethan Employee Management — Fix local login flag on seeded accounts
-- =============================================================================
-- The original foundation script seeded both fallback accounts with
-- LocalLoginEnabled = 0, which meant VerifyLocalLoginAsync's WHERE clause
-- (LocalUsername == username && LocalLoginEnabled && IsActive) could never
-- match them — the fallback login has never actually worked, regardless of
-- whether a password was set via the bootstrap page. Idempotent.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement_Dev;
GO

UPDATE dbo.UserAccount
SET LocalLoginEnabled = 1
WHERE LocalUsername IN ('admin', 'officeadmin');

PRINT 'Local login enabled on both fallback accounts.';
