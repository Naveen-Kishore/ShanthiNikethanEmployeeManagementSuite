-- =============================================================================
-- Shanthi Nikethan Employee Management — Seed data for July 2026
-- =============================================================================
-- Loads the 69 staff members from the July 2026 IOB salary files.
-- The salary amount from the PDF is loaded as NetPayOverride, since we don't
-- yet know the Gross Pay for each staff. Once Gross Pay is entered per staff
-- via the UI, the statutory calcs kick in and NetPayOverride can be cleared.
--
-- Designation is now just Teaching / NonTeaching (matching the two IOB CSV
-- files). Finer-grained roles (Office Admin, Driver, Cleaner, Aaya) go in the
-- new SubDesignation column — run 03-SubDesignationOptions-Schema.sql first so
-- these values appear in the dropdown too.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement;
GO

DECLARE @seed_actor_name NVARCHAR(200) = 'seed@import';
DECLARE @seed_actor_oid  NVARCHAR(100) = '00000000-0000-0000-0000-000000000000';
DECLARE @doj DATE = '2021-06-01'; -- assumed academic year start; edit per staff via UI

IF NOT EXISTS (SELECT 1 FROM dbo.Staff)
BEGIN
    INSERT INTO dbo.Staff (StaffCode, DisplayOrder, FirstName, Initial, DisplayName,
        Designation, SubDesignation, DateOfJoining, BankAccountNumber, BankMode, GrossPay, NetPayOverride,
        CreatedByObjectId, CreatedByDisplayName)
    VALUES
    ('SNM-T-001', 1, N'MALARKODI', N'A', N'MALARKODI A', 'Teaching', NULL, @doj, '097001000001962', 1, 0, 40000, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-002', 2, N'KESAVAN', N'M', N'KESAVAN M', 'Teaching', NULL, @doj, '097001000025958', 1, 0, 22586, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-003', 3, N'SHANMUGAM', N'S', N'SHANMUGAM S', 'Teaching', NULL, @doj, '097001000025931', 1, 0, 19296, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-004', 4, N'SELVAM', N'R', N'SELVAM R', 'Teaching', NULL, @doj, '097001000025928', 1, 0, 19856, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-005', 5, N'SELVAM', N'S', N'SELVAM S', 'Teaching', NULL, @doj, '097001000025209', 1, 0, 18750, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-006', 6, N'JAYALAKSHMI', N'M', N'JAYALAKSHMI M', 'Teaching', NULL, @doj, '097001000025926', 1, 0, 19296, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-007', 7, N'GOMATHI', N'L', N'GOMATHI L', 'Teaching', NULL, @doj, '097001000025929', 1, 0, 17651, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-008', 8, N'SARASWATHI', N'S', N'SARASWATHI S', 'Teaching', NULL, @doj, '097001000025935', 1, 0, 17000, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-009', 9, N'REVATHI', N'V', N'REVATHI V', 'Teaching', NULL, @doj, '097001000025936', 1, 0, 14500, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-010', 10, N'SANGEETHA', N'P', N'SANGEETHA P', 'Teaching', NULL, @doj, '097001000020003', 1, 0, 15500, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-011', 11, N'UTHAYAREKA', N'U', N'UTHAYAREKA U', 'Teaching', NULL, @doj, '097001000025932', 1, 0, 11500, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-012', 12, N'BHUVANESHWARI', N'S', N'BHUVANESHWARI S', 'Teaching', NULL, @doj, '097001000020151', 1, 0, 15000, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-013', 13, N'KAVITHA', N'S', N'KAVITHA S', 'Teaching', NULL, @doj, '097001000025833', 1, 0, 16335, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-014', 14, N'SUVITHA', N'G', N'SUVITHA G', 'Teaching', NULL, @doj, '097001000025186', 1, 0, 15000, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-015', 15, N'BHARADEESWARI', N'M', N'BHARADEESWARI M', 'Teaching', NULL, @doj, '097001000025948', 1, 0, 15865, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-016', 16, N'SUGANYA', N'D', N'SUGANYA D', 'Teaching', NULL, @doj, '097001000025188', 1, 0, 13500, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-017', 17, N'DIVIYAPRIYA', N'R', N'DIVIYAPRIYA R', 'Teaching', NULL, @doj, '097001000025333', 1, 0, 12686, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-018', 18, N'RAMYA', N'R', N'RAMYA R', 'Teaching', NULL, @doj, '097001000025941', 1, 0, 11500, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-019', 19, N'ANUSIYA', N'S', N'ANUSIYA S', 'Teaching', NULL, @doj, '097001000025949', 1, 0, 12169, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-020', 20, N'VIJAYAKUMARI', N'P', N'VIJAYAKUMARI P', 'Teaching', NULL, @doj, '097001000025940', 1, 0, 10227, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-021', 21, N'ABHINAYA', N'C', N'ABHINAYA C', 'Teaching', NULL, @doj, '097001000025925', 1, 0, 12308, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-022', 22, N'REVATHI', N'K', N'REVATHI K', 'Teaching', NULL, @doj, '097001000025953', 1, 0, 9500, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-023', 23, N'SUGANTHI', N'R', N'SUGANTHI R', 'Teaching', NULL, @doj, '097001000025960', 1, 0, 8000, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-024', 24, N'ELAMATHI', N'P', N'ELAMATHI P', 'Teaching', NULL, @doj, '097001000013646', 1, 0, 9250, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-025', 25, N'AHILA', N'K', N'AHILA K', 'Teaching', NULL, @doj, '097001000025930', 1, 0, 8000, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-026', 26, N'SUGANTHI R', N'K', N'SUGANTHI R K', 'Teaching', NULL, @doj, '097001000025934', 1, 0, 8000, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-027', 27, N'MENAKA', N'R', N'MENAKA R', 'Teaching', NULL, @doj, '097001000025945', 1, 0, 10759, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-028', 28, N'CHANDRAKALA', N'N', N'CHANDRAKALA N', 'Teaching', NULL, @doj, '097001000025964', 1, 0, 8285, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-029', 29, N'NATHIYA', N'P', N'NATHIYA P', 'Teaching', NULL, @doj, '097001000025191', 1, 0, 8500, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-030', 30, N'GOMATHI', N'A', N'GOMATHI A', 'Teaching', NULL, @doj, '097001000025933', 1, 0, 8000, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-031', 31, N'SHANMUGA PRIYA', N'S', N'SHANMUGA PRIYA S', 'Teaching', NULL, @doj, '097001000025194', 1, 0, 8000, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-032', 32, N'DHIVYA', N'V', N'DHIVYA V', 'Teaching', NULL, @doj, '097001000025187', 1, 0, 7500, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-033', 33, N'SUBA', N'K', N'SUBA K', 'Teaching', NULL, @doj, '097001000025951', 1, 0, 7500, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-034', 34, N'SANTHIYA', N'S', N'SANTHIYA S', 'Teaching', NULL, @doj, '097001000025208', 1, 0, 8500, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-035', 35, N'ASIA BANU', N'M', N'ASIA BANU M', 'Teaching', NULL, @doj, '097001000025927', 1, 0, 8000, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-036', 36, N'MEHALA', N'N', N'MEHALA N', 'Teaching', NULL, @doj, '097001000025190', 1, 0, 7500, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-037', 37, N'SURUTHI', N'B', N'SURUTHI B', 'Teaching', NULL, @doj, '097001000025952', 1, 0, 8000, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-038', 38, N'MANJU', N'M', N'MANJU M', 'Teaching', NULL, @doj, '097001000025937', 1, 0, 8000, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-039', 39, N'KAVIPRIYA', N'N', N'KAVIPRIYA N', 'Teaching', NULL, @doj, '097001000025938', 1, 0, 8000, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-040', 40, N'GAYATHRI', N'K', N'GAYATHRI K', 'Teaching', NULL, @doj, '097001000025943', 1, 0, 8285, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-041', 41, N'ANUPRIYA', N'P', N'ANUPRIYA P', 'Teaching', NULL, @doj, '097001000025939', 1, 0, 8750, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-042', 42, N'JAYANTHI', N'T', N'JAYANTHI T', 'Teaching', NULL, @doj, '097001000008992', 1, 0, 8250, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-043', 43, N'ABINAYA', N'G', N'ABINAYA G', 'Teaching', NULL, @doj, '097001000025947', 1, 0, 8250, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-044', 44, N'GEETHA', N'G', N'GEETHA G', 'Teaching', NULL, @doj, '097001000025944', 1, 0, 8500, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-045', 45, N'MEERA BAI', N'M', N'MEERA BAI M', 'Teaching', NULL, @doj, '097001000025195', 1, 0, 7500, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-046', 46, N'ARUNA', N'R', N'ARUNA R', 'Teaching', NULL, @doj, '097001000025193', 1, 0, 8300, @seed_actor_oid, @seed_actor_name),
    ('SNM-T-047', 47, N'KALAIMANI', N'', N'KALAIMANI', 'Teaching', NULL, @doj, '097001000026125', 1, 0, 7000, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-001', 1, N'ANANTHAN', N'K', N'ANANTHAN K', 'NonTeaching', N'Office Admin', @doj, '097001000001959', 1, 0, 40000, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-002', 2, N'JAYAM', N'P', N'JAYAM P', 'NonTeaching', NULL, @doj, '097001000025956', 1, 0, 7600, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-003', 3, N'SELLAM', N'S', N'SELLAM S', 'NonTeaching', NULL, @doj, '097001000025955', 1, 0, 5600, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-004', 4, N'RADHA', N'S', N'RADHA S', 'NonTeaching', NULL, @doj, '097001000025207', 1, 0, 7300, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-005', 5, N'LAKSHMI', N'D', N'LAKSHMI D', 'NonTeaching', NULL, @doj, '097001000025199', 1, 0, 7600, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-006', 6, N'KANNAN', N'S', N'KANNAN S', 'NonTeaching', NULL, @doj, '097001000025957', 1, 0, 7300, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-007', 7, N'ALAGU', N'', N'ALAGU', 'NonTeaching', N'Aaya', @doj, '097001000025433', 1, 0, 7300, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-008', 8, N'PRAKASH', N'M', N'PRAKASH M', 'NonTeaching', N'Driver', @doj, '097001000025204', 1, 0, 13750, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-009', 9, N'RAJENDIRAN', N'M', N'RAJENDIRAN M', 'NonTeaching', N'Driver', @doj, '097001000025200', 1, 0, 6800, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-010', 10, N'MUTHUKUMAR', N'R', N'MUTHUKUMAR R', 'NonTeaching', N'Cleaner', @doj, '097001000025434', 1, 0, 6300, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-011', 11, N'AMUTHAVALLI', N'K', N'AMUTHAVALLI K', 'NonTeaching', N'Aaya', @doj, '097001000025432', 1, 0, 8500, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-012', 12, N'BHUVANESH', N'K', N'BHUVANESH K', 'NonTeaching', N'Driver', @doj, '520291014669086', 2, 0, 18561, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-013', 13, N'KUMAR', N'S', N'KUMAR S', 'NonTeaching', N'Driver', @doj, '097001000025968', 1, 0, 14848, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-014', 14, N'SELLADURAI', N'P', N'SELLADURAI P', 'NonTeaching', NULL, @doj, '097001000030780', 1, 0, 14895, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-015', 15, N'RAVI', N'S', N'RAVI S', 'NonTeaching', N'Driver', @doj, '097001000026010', 1, 0, 10000, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-016', 16, N'RAMAMOORTHY', N'', N'RAMAMOORTHY', 'NonTeaching', N'Driver', @doj, '097001000026023', 1, 0, 12000, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-017', 17, N'PERIYASAMY', N'V', N'PERIYASAMY V', 'NonTeaching', N'Cleaner', @doj, '097001000014288', 1, 0, 7300, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-018', 18, N'SRINIVASAN', N'P', N'SRINIVASAN P', 'NonTeaching', N'Cleaner', @doj, '097001000025206', 1, 0, 3800, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-019', 19, N'CHITHRA', N'R', N'CHITHRA R', 'NonTeaching', N'Aaya', @doj, '097001000026024', 1, 0, 8000, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-020', 20, N'SELLAIYA', N'', N'SELLAIYA', 'NonTeaching', N'Cleaner', @doj, '097001000026021', 1, 0, 6300, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-021', 21, N'PARVATHI', N'K', N'PARVATHI K', 'NonTeaching', N'Aaya', @doj, '097001000026022', 1, 0, 6100, @seed_actor_oid, @seed_actor_name),
    ('SNM-N-022', 22, N'BALAMURUGAN', N'N', N'BALAMURUGAN N', 'NonTeaching', N'Driver', @doj, '097001000026073', 1, 0, 12000, @seed_actor_oid, @seed_actor_name);

    INSERT INTO dbo.AuditLog (ActorDisplayName, ActorObjectId, Module, EntityType, Action, Context)
    VALUES (@seed_actor_name, @seed_actor_oid, 'StaffProfile', 'Staff', 'Import',
        'Seeded 69 staff from July 2026 IOB payroll files. Salary loaded as NetPayOverride until GrossPay is set per staff.');
END
GO

PRINT 'Seeded 47 teaching + 22 non-teaching staff.';