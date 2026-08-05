-- =============================================================================
-- Shanthi Nikethan Employee Management — Backfill Gross Pay (v2, Net-anchored)
-- =============================================================================
-- Supersedes 06-Backfill-GrossPay-From-NetPay.sql.
--
-- Basic Wage is now calculated as 50% of Net Pay (not Gross Pay) — see
-- Modules/StaffProfile/Services/StatutorySalaryCalculator.cs for the reasoning
-- and the algebra. That change doesn't affect Basic/EPF/EPS/EDLI at all (they
-- now come straight from Net Pay), but it DOES change the relationship between
-- Gross and Net slightly, which matters for the Employee/Employer ESIC lines
-- (still Gross-based) and the Gross Pay figure itself shown in the app.
--
-- This is optional — the drift versus the old backfill is small (typically a
-- few rupees on ESIC), but this brings everyone's Gross Pay exactly in line
-- with what the app's live calculator would produce if you retyped their Net
-- Pay today.
--
-- Safe to run repeatedly. NetPayOverride is never touched.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement;
GO

DECLARE @EsicThreshold  DECIMAL(12,2) = 21000;
DECLARE @NetDivisor     DECIMAL(10,6) = 1.06;    -- 1 + (50% * 12%)
DECLARE @EsicEmpRate    DECIMAL(10,6) = 0.0075;

UPDATE dbo.Staff
SET GrossPay =
    CASE
        WHEN ROUND((@NetDivisor * NetPayOverride) / (1 - @EsicEmpRate), 2) <= @EsicThreshold
            THEN ROUND((@NetDivisor * NetPayOverride) / (1 - @EsicEmpRate), 2)
        ELSE ROUND(@NetDivisor * NetPayOverride, 2)
    END
WHERE NetPayOverride IS NOT NULL
  AND SoftDeletedAtUtc IS NULL;

PRINT CONCAT(@@ROWCOUNT, ' staff member(s) recalculated under the Net-anchored formula.');
GO

SELECT TOP 10 StaffCode, DisplayName, GrossPay, NetPayOverride
FROM dbo.Staff
WHERE SoftDeletedAtUtc IS NULL
ORDER BY DisplayOrder;
