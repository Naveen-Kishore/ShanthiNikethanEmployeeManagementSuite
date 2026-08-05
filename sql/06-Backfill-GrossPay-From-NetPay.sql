-- =============================================================================
-- Shanthi Nikethan Employee Management — Backfill Gross Pay from Net Pay
-- =============================================================================
-- Your July 2026 seed data only had each staff member's take-home (Net Pay),
-- loaded into NetPayOverride with GrossPay left at 0. That means every
-- Gross-derived figure (Basic Wage, both Employee deductions, all four
-- Employer contributions) shows ₹0 in the Salary tab — correct given
-- GrossPay is genuinely 0, but not useful to look at.
--
-- This is a ONE-TIME fix: for every staff member with GrossPay = 0 and a
-- known NetPayOverride, it works backward to find the Gross Pay that would
-- produce that exact Net Pay — the same formula used by the app's live
-- Gross <-> Net calculator (StatutorySalaryCalculator.InverseGrossFromNet).
--
-- NetPayOverride is left untouched, so the displayed Net Pay for each person
-- doesn't move by even a paisa — only GrossPay (and everything the app
-- derives from it) gets filled in.
--
-- Safe to run more than once — it only touches rows still sitting at
-- GrossPay = 0, so already-corrected staff are skipped automatically.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement;
GO

DECLARE @EsicThreshold DECIMAL(12,2) = 21000;
DECLARE @FactorWithEsic DECIMAL(10,6) = 0.9325;  -- 1 - (50% * 12%) - 0.75%
DECLARE @FactorNoEsic   DECIMAL(10,6) = 0.9400;  -- 1 - (50% * 12%)

UPDATE dbo.Staff
SET GrossPay =
    CASE
        WHEN ROUND(NetPayOverride / @FactorWithEsic, 2) <= @EsicThreshold
            THEN ROUND(NetPayOverride / @FactorWithEsic, 2)
        ELSE ROUND(NetPayOverride / @FactorNoEsic, 2)
    END
WHERE GrossPay = 0
  AND NetPayOverride IS NOT NULL
  AND SoftDeletedAtUtc IS NULL;

PRINT CONCAT(@@ROWCOUNT, ' staff member(s) backfilled.');
GO

-- Verify: spot-check a few
SELECT TOP 10 StaffCode, DisplayName, GrossPay, NetPayOverride
FROM dbo.Staff
WHERE SoftDeletedAtUtc IS NULL
ORDER BY DisplayOrder;
