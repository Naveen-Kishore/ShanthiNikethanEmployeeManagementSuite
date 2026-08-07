-- =============================================================================
-- Shanthi Nikethan Employee Management — Populate ESIC numbers
-- =============================================================================
-- Prerequisite: run after 10-Switch-To-Uan-Driven-Enrollment.sql (EPF UANs
-- already on file). Populates ESIC IP Numbers from the June 2026 ESIC
-- contribution report, for the 16 of 17 who appear there (Kesavan M doesn't
-- — his Gross is above the ₹21,000 line, consistent with him being absent
-- from the consultant's ESIC roster).
--
-- SUJATHA C from the same report is NOT included here — she isn't anywhere
-- in this system's 69-person roster, and per the conversation she's already
-- been relieved. That's a "tell the consultant to remove her" conversation,
-- not something this script or the app needs to touch.
--
-- Matched by the same name-correlation approach as the EPF UANs — verify
-- the SELECT at the bottom.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement_Dev;
GO

UPDATE dbo.Staff SET EsicNumber = '6382747011' WHERE StaffCode = 'SNM-T-003'; -- SHANMUGAM S (confident — exact match)
UPDATE dbo.Staff SET EsicNumber = '6382747042' WHERE StaffCode = 'SNM-T-004'; -- SELVAM R (confident — exact match)
UPDATE dbo.Staff SET EsicNumber = '6382747089' WHERE StaffCode = 'SNM-T-013'; -- KAVITHA S (confident — exact match)
UPDATE dbo.Staff SET EsicNumber = '6382747100' WHERE StaffCode = 'SNM-T-015'; -- BHARADEESWARI M (confident — exact match)
UPDATE dbo.Staff SET EsicNumber = '6382747145' WHERE StaffCode = 'SNM-T-027'; -- MENAKA R (confident — exact match)
UPDATE dbo.Staff SET EsicNumber = '6383568382' WHERE StaffCode = 'SNM-T-021'; -- ABHINAYA C (confident — exact match)
UPDATE dbo.Staff SET EsicNumber = '6382756566' WHERE StaffCode = 'SNM-T-019'; -- ANUSIYA S / ESIC report: S ANUSIYA (confident, reordered)

UPDATE dbo.Staff SET EsicNumber = '6382756594' WHERE StaffCode = 'SNM-T-020'; -- VIJAYAKUMARI P / ESIC: VIJAYAKUMARI AKILAKUMAR (likely)
UPDATE dbo.Staff SET EsicNumber = '6382756643' WHERE StaffCode = 'SNM-T-017'; -- DIVIYAPRIYA R / ESIC: DIVIYAPRIYA BABU (likely)
UPDATE dbo.Staff SET EsicNumber = '6382834621' WHERE StaffCode = 'SNM-N-013'; -- KUMAR S / ESIC: KUMAR SUNDARAM (likely)
UPDATE dbo.Staff SET EsicNumber = '6382834073' WHERE StaffCode = 'SNM-T-006'; -- JAYALAKSHMI M / ESIC: JAYALAKSHMI VELMURUGAN (likely)
UPDATE dbo.Staff SET EsicNumber = '6382834362' WHERE StaffCode = 'SNM-T-007'; -- GOMATHI L / ESIC: GOMATHI ELANGOVAN (per your earlier confirmation this is Gomathi L)
UPDATE dbo.Staff SET EsicNumber = '6382834394' WHERE StaffCode = 'SNM-T-028'; -- CHANDRAKALA N / ESIC: CHANDRAKALA PRASATH (likely)
UPDATE dbo.Staff SET EsicNumber = '6382834416' WHERE StaffCode = 'SNM-T-040'; -- GAYATHRI K / ESIC: GAYATHRI GANESH (likely)
UPDATE dbo.Staff SET EsicNumber = '6382834573' WHERE StaffCode = 'SNM-N-012'; -- BHUVANESH K / ESIC: BHUVANESH KRISHNASAMY (likely)
UPDATE dbo.Staff SET EsicNumber = '6382834596' WHERE StaffCode = 'SNM-N-014'; -- SELLADURAI P / ESIC: SELLADURAI (likely)
GO

-- Verify — check every name/IP-number pair actually matches the person you expect
SELECT StaffCode, DisplayName, EpfUan, EsicNumber
FROM dbo.Staff
WHERE EpfUan IS NOT NULL AND EpfUan != '' OR EsicNumber IS NOT NULL AND EsicNumber != ''
ORDER BY StaffCode;
