-- =============================================================================
-- Shanthi Nikethan Employee Management — Post-Setup Verification
-- =============================================================================
-- Run this AFTER completing 01→25, then 99-Seed, then re-running
-- 06, 07, 09, 10, 11, 13, 14 (in that order) against whichever database
-- you're checking. Change the USE line below to _Dev or _Prod as needed.
--
-- DESIGN NOTE for future modules: Section 1 is fully dynamic (reads from
-- sys.tables) and will automatically list any new table a future module
-- script creates — nothing to edit there. When you add a new module,
-- append a NEW numbered section below rather than editing Sections 1-4,
-- so this file's history stays legible as the schema grows. A template
-- for that is at the bottom.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement_Dev;  -- change to _Prod as needed
GO

PRINT '=== SECTION 1: Full table inventory (auto-detects future modules — no edits needed here ever) ===';
SELECT
    s.name          AS SchemaName,
    t.name          AS TableName,
    p.rows          AS ApproxRowCount,
    t.create_date   AS CreatedOn
FROM sys.tables t
JOIN sys.schemas s   ON t.schema_id = s.schema_id
JOIN sys.partitions p ON t.object_id = p.object_id AND p.index_id IN (0, 1)
ORDER BY t.create_date, s.name, t.name;
GO

PRINT '=== SECTION 2: Staff module — core counts ===';
-- NOTE: no IsEpfEnabled column check here — script 10 drops it entirely
-- and replaces it with UAN-driven enrollment. Enrolled = EpfUan populated.
SELECT
    COUNT(*)                                                              AS TotalStaff,
    SUM(CASE WHEN SoftDeletedAtUtc IS NULL THEN 1 ELSE 0 END)             AS ActiveStaff,
    SUM(CASE WHEN SoftDeletedAtUtc IS NULL AND GrossPay = 0 THEN 1 ELSE 0 END)
                                                                            AS ActiveStaff_StillZeroGrossPay_NeedsBackfill,
    SUM(CASE WHEN EpfUan IS NOT NULL AND EpfUan <> '' THEN 1 ELSE 0 END)   AS EpfEnrolledCount,
    SUM(CASE WHEN EsicNumber IS NOT NULL AND EsicNumber <> '' THEN 1 ELSE 0 END)
                                                                            AS EsicEnrolledCount
FROM dbo.Staff;
GO

PRINT '=== SECTION 3: Expected values per the original migration scripts ===';
PRINT 'ActiveStaff_StillZeroGrossPay_NeedsBackfill should be 0 (06/07 backfill everyone)';
PRINT 'EpfEnrolledCount should be 17 (per script 10''s comment header)';
PRINT 'EsicEnrolledCount should be 16 (per script 13''s comment header)';
GO

PRINT '=== SECTION 4: Spot-check known records referenced across the migration scripts ===';
SELECT StaffCode, DisplayName, GrossPay, NetPayOverride, EpfUan, EsicNumber
FROM dbo.Staff
WHERE StaffCode IN ('SNM-T-002', 'SNM-T-003', 'SNM-T-015', 'SNM-T-021')
ORDER BY StaffCode;
GO

PRINT '=== SECTION 5: Integrity checks ===';
-- SubDesignation values that don't exist in the lookup table (data drift)
SELECT DISTINCT st.SubDesignation AS OrphanedSubDesignation
FROM dbo.Staff st
WHERE st.SubDesignation IS NOT NULL
  AND NOT EXISTS (
      SELECT 1 FROM dbo.SubDesignationOption opt WHERE opt.Name = st.SubDesignation
  );

-- Active staff missing a bank account number (should be none — NOT NULL column,
-- this just double-checks nothing sneaked in as empty string)
SELECT StaffCode, DisplayName
FROM dbo.Staff
WHERE SoftDeletedAtUtc IS NULL AND (BankAccountNumber IS NULL OR BankAccountNumber = '');
GO

-- =============================================================================
-- SECTION 6+: TEMPLATE FOR FUTURE MODULES
-- =============================================================================
-- Copy this block, rename the section, fill in checks relevant to the new
-- module's tables (which Section 1 above will already have surfaced by name).
--
-- PRINT '=== SECTION 6: <Module Name> — core counts ===';
-- SELECT COUNT(*) AS Total<Thing> FROM dbo.<NewTable>;
-- GO
-- =============================================================================
