-- =============================================================================
-- Shanthi Nikethan Employee Management — Set real Gross Pay from EPFO report
-- =============================================================================
-- Replaces the back-calculated Gross Pay estimates for your 17 EPF-enrolled
-- staff with their REAL Gross Pay, taken directly from the June 2026 EPFO
-- ECR report. This is ground truth, not an estimate — use it in preference
-- to anything computed by the earlier backfill scripts (06/07).
--
-- NetPayOverride is deliberately left untouched. The verification query at
-- the bottom shows each person's real Gross Pay next to what Net Pay it
-- implies (Gross - Employee EPF, since all these Gross figures are above
-- or near the ESIC line) versus your currently stored NetPayOverride.
--
-- Most of these will show a gap — e.g. KESAVAN M's real Gross (21900)
-- implies a Net of 20586, but NetPayOverride is 22586, a ₹2,000 gap. This
-- is likely a real component of pay (an allowance, etc.) this app's simple
-- Gross → EPF → Net formula doesn't model — not something to silently
-- "fix". Review each gap and decide whether NetPayOverride should change,
-- person by person.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement;
GO

UPDATE dbo.Staff SET GrossPay = 14750 WHERE StaffCode = 'SNM-T-015'; -- BHARADEESWARI M
UPDATE dbo.Staff SET GrossPay = 18150 WHERE StaffCode = 'SNM-N-012'; -- BHUVANESH K
UPDATE dbo.Staff SET GrossPay = 7750  WHERE StaffCode = 'SNM-T-028'; -- CHANDRAKALA N
UPDATE dbo.Staff SET GrossPay = 11900 WHERE StaffCode = 'SNM-T-017'; -- DIVIYAPRIYA R
UPDATE dbo.Staff SET GrossPay = 7750  WHERE StaffCode = 'SNM-T-040'; -- GAYATHRI K
UPDATE dbo.Staff SET GrossPay = 16650 WHERE StaffCode = 'SNM-T-007'; -- GOMATHI L
UPDATE dbo.Staff SET GrossPay = 18400 WHERE StaffCode = 'SNM-T-006'; -- JAYALAKSHMI M
UPDATE dbo.Staff SET GrossPay = 15250 WHERE StaffCode = 'SNM-T-013'; -- KAVITHA S
UPDATE dbo.Staff SET GrossPay = 21900 WHERE StaffCode = 'SNM-T-002'; -- KESAVAN M
UPDATE dbo.Staff SET GrossPay = 14200 WHERE StaffCode = 'SNM-N-013'; -- KUMAR S
UPDATE dbo.Staff SET GrossPay = 9850  WHERE StaffCode = 'SNM-T-027'; -- MENAKA R
UPDATE dbo.Staff SET GrossPay = 11350 WHERE StaffCode = 'SNM-T-019'; -- ANUSIYA S
UPDATE dbo.Staff SET GrossPay = 14250 WHERE StaffCode = 'SNM-N-014'; -- SELLADURAI P
UPDATE dbo.Staff SET GrossPay = 17400 WHERE StaffCode = 'SNM-T-004'; -- SELVAM R
UPDATE dbo.Staff SET GrossPay = 18400 WHERE StaffCode = 'SNM-T-003'; -- SHANMUGAM S
UPDATE dbo.Staff SET GrossPay = 9550  WHERE StaffCode = 'SNM-T-020'; -- VIJAYAKUMARI P
UPDATE dbo.Staff SET GrossPay = 11700 WHERE StaffCode = 'SNM-T-021'; -- ABHINAYA C
GO

-- Review gaps: RealNetImplied should roughly equal NetPayOverride. Where it
-- doesn't, decide person by person whether to update NetPayOverride.
SELECT
    StaffCode,
    DisplayName,
    GrossPay AS RealGrossPay,
    ROUND(GrossPay - ROUND(ROUND(GrossPay * 0.5, 0) * 0.12, 0), 2) AS RealNetImplied,
    NetPayOverride AS CurrentlyStoredNetPay,
    NetPayOverride - ROUND(GrossPay - ROUND(ROUND(GrossPay * 0.5, 0) * 0.12, 0), 2) AS Gap
FROM dbo.Staff
WHERE EpfUan IS NOT NULL AND EpfUan != ''
ORDER BY ABS(NetPayOverride - ROUND(GrossPay - ROUND(ROUND(GrossPay * 0.5, 0) * 0.12, 0), 2)) DESC;
