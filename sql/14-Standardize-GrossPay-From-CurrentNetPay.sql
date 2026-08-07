-- =============================================================================
-- Shanthi Nikethan Employee Management — Standardize Gross Pay from Net Pay
-- =============================================================================
-- SUPERSEDES 11-Set-Real-GrossPay-From-Epfo-Report.sql.
--
-- That script anchored Gross Pay to the June 2026 EPFO-filed figures. Since
-- then, Net Pay has increased for these staff (raises) without the EPF/ESIC
-- basis being revised to match — which is exactly the mismatch you diagnosed.
-- Going forward, the standard is:
--
--   Net Pay is fixed for the academic year (the source of truth).
--   Gross Pay is DERIVED from it using the textbook formula, always.
--   EPF applies if EpfUan is on file. ESIC applies if EsicNumber is on file.
--
-- This script recomputes Gross Pay for every staff member with EPF and/or
-- ESIC enrollment, solving backward from their CURRENT NetPayOverride —
-- the exact same formula the app's live "type Net Pay, Gross auto-solves"
-- feature already uses (StatutorySalaryCalculator.InverseGrossFromNet).
--
-- NetPayOverride is never touched — only Gross Pay adjusts, per your
-- instruction. This WILL increase computed EPF/ESIC contributions for
-- these 17 versus what's been filed so far, which is the intended,
-- compliance-correct outcome — not a bug to "fix" back down.
--
-- RUN THIS AGAIN any time Net Pay changes in bulk for EPF/ESIC-enrolled
-- staff (e.g. next academic year's revision) — it's fully repeatable and
-- safe to run as often as needed; it only recalculates, never destroys data.
--
-- Prerequisite: run 10 (EPF UANs) and 13 (ESIC numbers) first, so this
-- script knows who's enrolled in what.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement_Dev;
GO

DECLARE @EpfRate  DECIMAL(10,6) = 0.06;    -- 50% (Basic) * 12% (Employee EPF)
DECLARE @EsicRate DECIMAL(10,6) = 0.0075;  -- Employee ESIC

UPDATE dbo.Staff
SET GrossPay = ROUND(
    NetPayOverride / (
        1
        - CASE WHEN EpfUan IS NOT NULL AND EpfUan != '' THEN @EpfRate ELSE 0 END
        - CASE WHEN EsicNumber IS NOT NULL AND EsicNumber != '' THEN @EsicRate ELSE 0 END
    ), 2)
WHERE NetPayOverride IS NOT NULL
  AND SoftDeletedAtUtc IS NULL
  AND (
        (EpfUan IS NOT NULL AND EpfUan != '')
     OR (EsicNumber IS NOT NULL AND EsicNumber != '')
      );

PRINT CONCAT(@@ROWCOUNT, ' staff member(s) standardized.');
GO

-- Verify: for each person, show old vs new Gross, and confirm Basic/EPF/EPS/ER
-- using the SAME whole-rupee-rounding the app itself uses
SELECT
    StaffCode,
    DisplayName,
    GrossPay AS NewGrossPay,
    NetPayOverride AS NetPay,
    CASE WHEN EpfUan IS NOT NULL AND EpfUan != '' THEN 'Yes' ELSE 'No' END AS EpfEnrolled,
    CASE WHEN EsicNumber IS NOT NULL AND EsicNumber != '' THEN 'Yes' ELSE 'No' END AS EsicEnrolled,
    ROUND(GrossPay * 0.5, 0) AS BasicWage,
    ROUND(ROUND(GrossPay * 0.5, 0) * 0.12, 0) AS EmployeeEpf
FROM dbo.Staff
WHERE (EpfUan IS NOT NULL AND EpfUan != '') OR (EsicNumber IS NOT NULL AND EsicNumber != '')
ORDER BY StaffCode;
