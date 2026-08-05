-- =============================================================================
-- Shanthi Nikethan Employee Management — Staff Profile Module Schema
-- =============================================================================
-- Prerequisite: run 01-Core-Schema.sql first.
--
-- Design notes:
--   * Staff is the master record. Never hard-deleted from the UI — instead
--     SoftDeletedAtUtc is set, and a background sweep purges records
--     older than 60 days.
--   * The 60-day window allows accidental deletions to be reversed
--     from within the app while preserving all previous data.
--   * All salary calculations (Basic, EPF, ESIC, etc.) are derived from
--     GrossPay at runtime — we do NOT store them in the DB, so if the
--     statutory percentages change (e.g. EPS cap moves from 15000), no
--     data migration is needed.
--   * PAN and Aadhaar are stored plaintext; the UI masks them.
--     For production compliance with DPDPA 2023, consider column-level
--     encryption. For a 69-staff school on a private VM, plaintext is
--     acceptable if the DB file is encrypted at rest (BitLocker).
--   * EPF Password is stored plaintext. Same caveat as above. Users
--     can enter their portal password so admins can log in on their
--     behalf for statutory filings — this is a real workflow need in
--     Indian schools.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement;
GO

IF OBJECT_ID('dbo.Staff', 'U') IS NOT NULL DROP TABLE dbo.Staff;
GO

CREATE TABLE dbo.Staff (
    -- === Identity ===
    Id                          UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
    StaffCode                   NVARCHAR(20)        NOT NULL,           -- human-readable, e.g. "SNM-T-047"
    DisplayOrder                INT                 NOT NULL DEFAULT 0, -- controls S.No in payroll outputs

    -- === Personal ===
    FirstName                   NVARCHAR(100)       NOT NULL,
    Initial                     NVARCHAR(10)        NULL,
    DisplayName                 NVARCHAR(150)       NOT NULL,           -- "MALARKODI A" — matches bank/PF records
    PhotoRelativePath           NVARCHAR(500)       NULL,               -- e.g. "uploads/photos/<guid>.jpg"

    -- === Contact ===
    EmailAddress                NVARCHAR(150)       NULL,
    PhoneNumber                 NVARCHAR(20)        NULL,
    AlternatePhoneNumber        NVARCHAR(20)        NULL,
    WhatsappNumber              NVARCHAR(20)        NULL,
    CompleteAddress             NVARCHAR(MAX)       NULL,
    BusNumber                   NVARCHAR(20)        NULL,               -- for school transport mapping

    -- === Employment ===
    Designation                 NVARCHAR(30)        NOT NULL,           -- Teaching / NonTeaching
    SubDesignation              NVARCHAR(50)        NULL,               -- e.g. "Office Admin", "Driver", "Cleaner", "Aaya" — free text, admin-managed list
    DateOfJoining               DATE                NOT NULL,

    -- === Statutory IDs ===
    PanNumber                   NVARCHAR(15)        NULL,               -- 10 chars usually
    AadhaarNumber               NVARCHAR(20)        NULL,               -- 12 digits usually
    EpfUan                      NVARCHAR(20)        NULL,               -- 12 digits
    EpfPassword                 NVARCHAR(200)       NULL,               -- see caveat above
    EsicNumber                  NVARCHAR(20)        NULL,

    -- === Banking ===
    BankAccountNumber           NVARCHAR(30)        NOT NULL,
    BankIfscCode                NVARCHAR(15)        NULL,
    BankPassbookRelativePath    NVARCHAR(500)       NULL,               -- scanned image
    BankMode                    TINYINT             NOT NULL DEFAULT 1, -- 1=IOB (in bulk CSV), 2=Manual/NEFT

    -- === Salary base ===
    -- Only GrossPay and NetPayOverride are stored.
    -- All statutory calcs (Basic, EPF, ESIC, EPS, EDLI, Employer contribs) are
    -- computed at runtime in the SalaryCalculator service.
    GrossPay                    DECIMAL(12, 2)      NOT NULL DEFAULT 0,
    -- If NetPayOverride is set, it's used verbatim (e.g. during transition
    -- when we only know the Net Pay agreed with the staff, not Gross yet).
    NetPayOverride              DECIMAL(12, 2)      NULL,

    -- === Meta / lifecycle ===
    IsActive                    BIT                 NOT NULL DEFAULT 1,
    SoftDeletedAtUtc            DATETIME2           NULL,               -- set on soft-delete; NULL means active
    SoftDeleteReason            NVARCHAR(500)       NULL,
    CreatedAtUtc                DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedByObjectId           NVARCHAR(100)       NOT NULL,
    CreatedByDisplayName        NVARCHAR(200)       NOT NULL,
    LastModifiedAtUtc           DATETIME2           NULL,
    LastModifiedByObjectId      NVARCHAR(100)       NULL,
    LastModifiedByDisplayName   NVARCHAR(200)       NULL,

    -- === Constraints ===
    CONSTRAINT UQ_Staff_StaffCode        UNIQUE (StaffCode),
    CONSTRAINT UQ_Staff_BankAccount      UNIQUE (BankAccountNumber),
    CONSTRAINT CK_Staff_Designation      CHECK (Designation IN ('Teaching', 'NonTeaching')),
    CONSTRAINT CK_Staff_BankMode         CHECK (BankMode IN (1, 2)),
    CONSTRAINT CK_Staff_GrossPay         CHECK (GrossPay >= 0),
    CONSTRAINT CK_Staff_NetPayOverride   CHECK (NetPayOverride IS NULL OR NetPayOverride >= 0)
);

CREATE INDEX IX_Staff_Designation        ON dbo.Staff (Designation) WHERE SoftDeletedAtUtc IS NULL;
CREATE INDEX IX_Staff_Active_Order       ON dbo.Staff (IsActive, DisplayOrder) WHERE SoftDeletedAtUtc IS NULL;
CREATE INDEX IX_Staff_SoftDeleted        ON dbo.Staff (SoftDeletedAtUtc) WHERE SoftDeletedAtUtc IS NOT NULL;
CREATE INDEX IX_Staff_Search             ON dbo.Staff (DisplayName, PhoneNumber, EmailAddress) WHERE SoftDeletedAtUtc IS NULL;
GO

-- =============================================================================
-- View: active staff only, for the default directory
-- =============================================================================
IF OBJECT_ID('dbo.vw_ActiveStaff', 'V') IS NOT NULL DROP VIEW dbo.vw_ActiveStaff;
GO
CREATE VIEW dbo.vw_ActiveStaff AS
SELECT * FROM dbo.Staff WHERE SoftDeletedAtUtc IS NULL AND IsActive = 1;
GO

PRINT 'Staff Profile schema created successfully.';
PRINT 'Next (optional): run 99-Seed-July-2026.sql to load your 69 staff.';
