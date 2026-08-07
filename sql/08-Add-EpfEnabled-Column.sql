-- =============================================================================
-- Shanthi Nikethan Employee Management — Add IsEpfEnabled column
-- =============================================================================
-- Safe to run on your existing database — only adds a column, doesn't touch
-- any existing data. Everyone defaults to IsEpfEnabled = 0 (not enrolled);
-- see 09-Enable-EPF-For-Known-Staff.sql to turn it on for your current
-- 17 EPF-enrolled staff.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement_Dev;
GO

IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Staff') AND name = 'IsEpfEnabled'
)
BEGIN
    ALTER TABLE dbo.Staff ADD IsEpfEnabled BIT NOT NULL DEFAULT 0;
    PRINT 'Added IsEpfEnabled column (defaulted to 0 / not enrolled for all existing staff).';
END
ELSE
BEGIN
    PRINT 'IsEpfEnabled column already exists — nothing to do.';
END
GO
