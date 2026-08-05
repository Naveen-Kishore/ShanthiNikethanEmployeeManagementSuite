-- =============================================================================
-- Shanthi Nikethan Employee Management — Switch to UAN/ESIC-driven enrollment
-- =============================================================================
-- Replaces the IsEpfEnabled toggle with a simpler model: EPF/ESIC enrollment
-- is now derived from whether an EPF UAN / ESIC number is on file, rather
-- than a separate switch to keep in sync.
--
-- Step 1 drops the now-unused column. Step 2 populates real UAN numbers for
-- your 17 EPF-enrolled staff, taken directly from the June 2026 EPFO ECR
-- report — same 17 people confirmed in 09-Enable-EPF-For-Known-Staff.sql,
-- now with their actual UAN instead of just a yes/no flag.
--
-- 12 of these 17 are matched by first name only (see the conversation for
-- the full likely/confident breakdown) — verify the SELECT at the bottom
-- before trusting this for anything compliance-relevant.
--
-- ESIC numbers aren't included — your report didn't have an ESIC column.
-- Add those individually via each profile's Statutory tab as you get them.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement;
GO

-- Step 1: Drop the old toggle column, if present. SQL Server auto-creates a
-- "default constraint" for any column with a DEFAULT value, and that must be
-- dropped before the column itself can be.
IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Staff') AND name = 'IsEpfEnabled'
)
BEGIN
    DECLARE @constraintName NVARCHAR(200);
    SELECT @constraintName = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.Staff') AND c.name = 'IsEpfEnabled';

    IF @constraintName IS NOT NULL
        EXEC('ALTER TABLE dbo.Staff DROP CONSTRAINT ' + @constraintName);

    ALTER TABLE dbo.Staff DROP COLUMN IsEpfEnabled;
    PRINT 'Dropped IsEpfEnabled column.';
END
GO

-- Step 2: Populate real UAN numbers for the 17 EPF-enrolled staff
UPDATE dbo.Staff SET EpfUan = '100207836492' WHERE StaffCode = 'SNM-T-015'; -- BHARADEESWARI M / EPFO: BHARADEESWARI SARAVANAN
UPDATE dbo.Staff SET EpfUan = '100181035610' WHERE StaffCode = 'SNM-N-012'; -- BHUVANESH K / EPFO: BHUVANESH KRISHNASAMY
UPDATE dbo.Staff SET EpfUan = '100823288694' WHERE StaffCode = 'SNM-T-028'; -- CHANDRAKALA N / EPFO: CHANDRAKALA PRASATH
UPDATE dbo.Staff SET EpfUan = '100833916566' WHERE StaffCode = 'SNM-T-017'; -- DIVIYAPRIYA R / EPFO: DIVIYAPRIYA BABU
UPDATE dbo.Staff SET EpfUan = '100824286317' WHERE StaffCode = 'SNM-T-040'; -- GAYATHRI K / EPFO: GAYATHRI GANESH
UPDATE dbo.Staff SET EpfUan = '100201651996' WHERE StaffCode = 'SNM-T-007'; -- GOMATHI L (confirmed)
UPDATE dbo.Staff SET EpfUan = '100208081657' WHERE StaffCode = 'SNM-T-006'; -- JAYALAKSHMI M / EPFO: JAYALAKSHMI VELMURUGAN
UPDATE dbo.Staff SET EpfUan = '100321886971' WHERE StaffCode = 'SNM-T-013'; -- KAVITHA S / EPFO: KAVITHA RAGUPATHI
UPDATE dbo.Staff SET EpfUan = '100208192587' WHERE StaffCode = 'SNM-T-002'; -- KESAVAN M (confident)
UPDATE dbo.Staff SET EpfUan = '100321891940' WHERE StaffCode = 'SNM-N-013'; -- KUMAR S / EPFO: KUMAR SUNDARAM
UPDATE dbo.Staff SET EpfUan = '100840408827' WHERE StaffCode = 'SNM-T-027'; -- MENAKA R / EPFO: MENAKA RAGUPATHI
UPDATE dbo.Staff SET EpfUan = '100321396046' WHERE StaffCode = 'SNM-T-019'; -- ANUSIYA S / EPFO: S ANUSIYA (confident)
UPDATE dbo.Staff SET EpfUan = '100839767613' WHERE StaffCode = 'SNM-N-014'; -- SELLADURAI P / EPFO: SELLADURAI
UPDATE dbo.Staff SET EpfUan = '100287483789' WHERE StaffCode = 'SNM-T-004'; -- SELVAM R (confident)
UPDATE dbo.Staff SET EpfUan = '100322727935' WHERE StaffCode = 'SNM-T-003'; -- SHANMUGAM S / EPFO: SHANMUGAM SELLAMUTHU
UPDATE dbo.Staff SET EpfUan = '100833323383' WHERE StaffCode = 'SNM-T-020'; -- VIJAYAKUMARI P / EPFO: VIJAYAKUMARI AKILAKUMAR
UPDATE dbo.Staff SET EpfUan = '102247074809' WHERE StaffCode = 'SNM-T-021'; -- ABHINAYA C (confident)
GO

-- Verify — check every name/UAN pair actually matches the person you expect
SELECT StaffCode, DisplayName, EpfUan
FROM dbo.Staff
WHERE EpfUan IS NOT NULL AND EpfUan != ''
ORDER BY StaffCode;
