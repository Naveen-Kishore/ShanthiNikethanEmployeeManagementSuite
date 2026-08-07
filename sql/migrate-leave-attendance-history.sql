-- =============================================================================
-- Migrate Leave & Attendance history: original database -> _Dev and _Prod
-- =============================================================================
-- WHY THIS ISN'T A SIMPLE "COPY ALL TABLES":
-- Staff.Id uses NEWSEQUENTIALID() - a different random value was generated
-- every time 99-Seed-July-2026.sql ran, so the original database, _Dev, and
-- _Prod all have DIFFERENT GUIDs for the same person. A raw copy of
-- LeaveRecord/AttendanceRecord rows would carry StaffId values that point to
-- nothing (or nobody) in the target database. This script translates StaffId
-- via StaffCode instead (a stable, human-readable key like 'SNM-T-002' that's
-- the same everywhere) - both tables conveniently already store StaffCode as
-- a plain column, so this join is straightforward.
--
-- DELIBERATELY SCOPED to only these two tables. NOT Staff, NOT UserAccount,
-- NOT RoleGroup, NOT anything configuration-related - those are already
-- correctly and independently set up in _Dev/_Prod, and copying them over
-- would undo the Dev/Prod separation and RBAC work already done.
--
-- SAFE TO RE-RUN: each INSERT checks for existing matching rows first, so
-- running this twice won't create duplicates.
--
-- COPIES, DOES NOT DELETE: the original database is never modified by this
-- script. Nothing here removes data from it.
-- =============================================================================


-- #############################################################################
-- # TARGET: ShanthiNikethanEmployeeManagement_Dev
-- #############################################################################
USE ShanthiNikethanEmployeeManagement_Dev;
GO

PRINT '=== Migrating into _Dev ===';

-- Pre-flight: any source staff with no matching StaffCode in this target?
-- These rows will be silently skipped by the INSERTs below - see this list
-- first so you know who's missing before assuming the migration is complete.
PRINT 'Staff in source Leave/Attendance history with NO match in _Dev.Staff (will be skipped):';
SELECT DISTINCT src.StaffCode, src.StaffDisplayName, 'LeaveRecord' AS FoundIn
FROM ShanthiNikethanEmployeeManagement.dbo.LeaveRecord src
WHERE NOT EXISTS (SELECT 1 FROM dbo.Staff t WHERE t.StaffCode = src.StaffCode)
UNION
SELECT DISTINCT src.StaffCode, src.StaffDisplayName, 'AttendanceRecord' AS FoundIn
FROM ShanthiNikethanEmployeeManagement.dbo.AttendanceRecord src
WHERE NOT EXISTS (SELECT 1 FROM dbo.Staff t WHERE t.StaffCode = src.StaffCode);

-- ---- Leave records ----
INSERT INTO dbo.LeaveRecord (
    Id, StaffId, StaffCode, StaffDisplayName, Designation,
    StartDate, EndDate, DaysCount, Reason, SubstituteArrangementNotes,
    CreatedAtUtc, CreatedByObjectId, CreatedByDisplayName
)
SELECT
    NEWID(),                 -- new Id in this database - don't reuse the source Id
    target_staff.Id,         -- translated StaffId, via StaffCode match
    src.StaffCode, src.StaffDisplayName, src.Designation,
    src.StartDate, src.EndDate, src.DaysCount, src.Reason, src.SubstituteArrangementNotes,
    src.CreatedAtUtc, src.CreatedByObjectId, src.CreatedByDisplayName
FROM ShanthiNikethanEmployeeManagement.dbo.LeaveRecord src
INNER JOIN dbo.Staff target_staff ON target_staff.StaffCode = src.StaffCode
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.LeaveRecord existing
    WHERE existing.StaffCode = src.StaffCode
      AND existing.StartDate = src.StartDate
      AND existing.EndDate = src.EndDate
);
PRINT CONCAT(@@ROWCOUNT, ' leave record(s) inserted into _Dev (duplicates/unmatched staff skipped).');

-- ---- Attendance records ----
INSERT INTO dbo.AttendanceRecord (
    Id, StaffId, StaffCode, StaffDisplayName, Designation,
    AttendanceDate, MorningStatus, EveningStatus, Notes,
    IsSystemGenerated, IsAdminOverride,
    CreatedAtUtc, CreatedByObjectId, CreatedByDisplayName,
    LastModifiedAtUtc, LastModifiedByObjectId, LastModifiedByDisplayName
)
SELECT
    NEWID(),
    target_staff.Id,
    src.StaffCode, src.StaffDisplayName, src.Designation,
    src.AttendanceDate, src.MorningStatus, src.EveningStatus, src.Notes,
    src.IsSystemGenerated, src.IsAdminOverride,
    src.CreatedAtUtc, src.CreatedByObjectId, src.CreatedByDisplayName,
    src.LastModifiedAtUtc, src.LastModifiedByObjectId, src.LastModifiedByDisplayName
FROM ShanthiNikethanEmployeeManagement.dbo.AttendanceRecord src
INNER JOIN dbo.Staff target_staff ON target_staff.StaffCode = src.StaffCode
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.AttendanceRecord existing
    WHERE existing.StaffCode = src.StaffCode
      AND existing.AttendanceDate = src.AttendanceDate
);
PRINT CONCAT(@@ROWCOUNT, ' attendance record(s) inserted into _Dev (duplicates/unmatched staff skipped).');
GO


-- #############################################################################
-- # TARGET: ShanthiNikethanEmployeeManagement_Prod
-- #############################################################################
USE ShanthiNikethanEmployeeManagement_Prod;
GO

PRINT '=== Migrating into _Prod ===';

PRINT 'Staff in source Leave/Attendance history with NO match in _Prod.Staff (will be skipped):';
SELECT DISTINCT src.StaffCode, src.StaffDisplayName, 'LeaveRecord' AS FoundIn
FROM ShanthiNikethanEmployeeManagement.dbo.LeaveRecord src
WHERE NOT EXISTS (SELECT 1 FROM dbo.Staff t WHERE t.StaffCode = src.StaffCode)
UNION
SELECT DISTINCT src.StaffCode, src.StaffDisplayName, 'AttendanceRecord' AS FoundIn
FROM ShanthiNikethanEmployeeManagement.dbo.AttendanceRecord src
WHERE NOT EXISTS (SELECT 1 FROM dbo.Staff t WHERE t.StaffCode = src.StaffCode);

-- ---- Leave records ----
INSERT INTO dbo.LeaveRecord (
    Id, StaffId, StaffCode, StaffDisplayName, Designation,
    StartDate, EndDate, DaysCount, Reason, SubstituteArrangementNotes,
    CreatedAtUtc, CreatedByObjectId, CreatedByDisplayName
)
SELECT
    NEWID(),
    target_staff.Id,
    src.StaffCode, src.StaffDisplayName, src.Designation,
    src.StartDate, src.EndDate, src.DaysCount, src.Reason, src.SubstituteArrangementNotes,
    src.CreatedAtUtc, src.CreatedByObjectId, src.CreatedByDisplayName
FROM ShanthiNikethanEmployeeManagement.dbo.LeaveRecord src
INNER JOIN dbo.Staff target_staff ON target_staff.StaffCode = src.StaffCode
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.LeaveRecord existing
    WHERE existing.StaffCode = src.StaffCode
      AND existing.StartDate = src.StartDate
      AND existing.EndDate = src.EndDate
);
PRINT CONCAT(@@ROWCOUNT, ' leave record(s) inserted into _Prod (duplicates/unmatched staff skipped).');

-- ---- Attendance records ----
INSERT INTO dbo.AttendanceRecord (
    Id, StaffId, StaffCode, StaffDisplayName, Designation,
    AttendanceDate, MorningStatus, EveningStatus, Notes,
    IsSystemGenerated, IsAdminOverride,
    CreatedAtUtc, CreatedByObjectId, CreatedByDisplayName,
    LastModifiedAtUtc, LastModifiedByObjectId, LastModifiedByDisplayName
)
SELECT
    NEWID(),
    target_staff.Id,
    src.StaffCode, src.StaffDisplayName, src.Designation,
    src.AttendanceDate, src.MorningStatus, src.EveningStatus, src.Notes,
    src.IsSystemGenerated, src.IsAdminOverride,
    src.CreatedAtUtc, src.CreatedByObjectId, src.CreatedByDisplayName,
    src.LastModifiedAtUtc, src.LastModifiedByObjectId, src.LastModifiedByDisplayName
FROM ShanthiNikethanEmployeeManagement.dbo.AttendanceRecord src
INNER JOIN dbo.Staff target_staff ON target_staff.StaffCode = src.StaffCode
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.AttendanceRecord existing
    WHERE existing.StaffCode = src.StaffCode
      AND existing.AttendanceDate = src.AttendanceDate
);
PRINT CONCAT(@@ROWCOUNT, ' attendance record(s) inserted into _Prod (duplicates/unmatched staff skipped).');
GO


-- #############################################################################
-- # VERIFY: row counts across all three databases, side by side
-- #############################################################################
SELECT 'Original' AS Database_, 'LeaveRecord' AS Table_, COUNT(*) AS RowCount FROM ShanthiNikethanEmployeeManagement.dbo.LeaveRecord
UNION ALL
SELECT 'Original', 'AttendanceRecord', COUNT(*) FROM ShanthiNikethanEmployeeManagement.dbo.AttendanceRecord
UNION ALL
SELECT '_Dev', 'LeaveRecord', COUNT(*) FROM ShanthiNikethanEmployeeManagement_Dev.dbo.LeaveRecord
UNION ALL
SELECT '_Dev', 'AttendanceRecord', COUNT(*) FROM ShanthiNikethanEmployeeManagement_Dev.dbo.AttendanceRecord
UNION ALL
SELECT '_Prod', 'LeaveRecord', COUNT(*) FROM ShanthiNikethanEmployeeManagement_Prod.dbo.LeaveRecord
UNION ALL
SELECT '_Prod', 'AttendanceRecord', COUNT(*) FROM ShanthiNikethanEmployeeManagement_Prod.dbo.AttendanceRecord
ORDER BY Table_, Database_;
