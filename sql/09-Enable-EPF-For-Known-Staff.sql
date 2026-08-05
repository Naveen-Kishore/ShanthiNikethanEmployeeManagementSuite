-- =============================================================================
-- Shanthi Nikethan Employee Management — Enable EPF for known staff
-- =============================================================================
-- Prerequisite: run 08-Add-EpfEnabled-Column.sql first.
--
-- Turns on IsEpfEnabled for the 17 staff currently EPF-enrolled per the
-- June 2026 EPFO ECR contribution report. Matched by StaffCode, verified
-- against the report's Gross/EPF/EE/EPS/ER figures (see conversation).
--
-- IMPORTANT: 12 of these 17 are matched by first name only, since the
-- EPFO report uses fuller names (often a spouse's/father's name) that
-- don't exactly match how this system's records were seeded from the
-- original bank CSVs. Confirm the DisplayName in the SELECT output at
-- the bottom actually matches before trusting this for anything
-- compliance-relevant.
--
-- GOMATHI is deliberately left OUT — there are two candidates (GOMATHI L,
-- SNM-T-007, and GOMATHI A, SNM-T-030) and no way to tell which one from
-- the report alone. Uncomment the correct line below once you know.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement;
GO

UPDATE dbo.Staff SET IsEpfEnabled = 1
WHERE StaffCode IN (
    'SNM-T-002',  -- KESAVAN M                (confident — exact match)
    'SNM-T-004',  -- SELVAM R                 (confident — exact match)
    'SNM-T-021',  -- ABHINAYA C               (confident — exact match)
    'SNM-T-019',  -- ANUSIYA S  / "S ANUSIYA" (confident — same name, reordered)

    'SNM-T-015',  -- BHARADEESWARI M          (likely) — EPFO: BHARADEESWARI SARAVANAN
    'SNM-N-012',  -- BHUVANESH K              (likely) — EPFO: BHUVANESH KRISHNASAMY
    'SNM-T-028',  -- CHANDRAKALA N            (likely) — EPFO: CHANDRAKALA PRASATH
    'SNM-T-017',  -- DIVIYAPRIYA R            (likely) — EPFO: DIVIYAPRIYA BABU
    'SNM-T-040',  -- GAYATHRI K               (likely) — EPFO: GAYATHRI GANESH
    'SNM-T-006',  -- JAYALAKSHMI M            (likely) — EPFO: JAYALAKSHMI VELMURUGAN
    'SNM-T-013',  -- KAVITHA S                (likely) — EPFO: KAVITHA RAGUPATHI
    'SNM-N-013',  -- KUMAR S                  (likely) — EPFO: KUMAR SUNDARAM
    'SNM-T-027',  -- MENAKA R                 (likely) — EPFO: MENAKA RAGUPATHI
    'SNM-N-014',  -- SELLADURAI P             (likely) — EPFO: SELLADURAI
    'SNM-T-003',  -- SHANMUGAM S              (likely) — EPFO: SHANMUGAM SELLAMUTHU
    'SNM-T-020',  -- VIJAYAKUMARI P           (likely) — EPFO: VIJAYAKUMARI AKILAKUMAR

    'SNM-T-007'   -- GOMATHI L                (confirmed by user)
);
GO

-- Verify — check every name below actually matches the person you expect
SELECT StaffCode, DisplayName, Designation, SubDesignation, IsEpfEnabled
FROM dbo.Staff
WHERE IsEpfEnabled = 1
ORDER BY StaffCode;
