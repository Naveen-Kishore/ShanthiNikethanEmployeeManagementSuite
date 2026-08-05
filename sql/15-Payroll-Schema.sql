-- =============================================================================
-- Shanthi Nikethan Employee Management — Payroll Module Schema
-- =============================================================================
-- Creates the two tables backing the Payroll module: PayrollRun (one row
-- per monthly cycle, Draft until Published) and PayrollLineItem (a frozen
-- per-staff snapshot for that run).
--
-- Once a PayrollRun is Published, its line items must never be edited —
-- the app enforces this, but if you ever query this table directly, treat
-- published rows as read-only history.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement;
GO

IF OBJECT_ID('dbo.PayrollLineItem', 'U') IS NOT NULL DROP TABLE dbo.PayrollLineItem;
IF OBJECT_ID('dbo.PayrollRun', 'U') IS NOT NULL DROP TABLE dbo.PayrollRun;
GO

CREATE TABLE dbo.PayrollRun (
    Id                          UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY DEFAULT NEWID(),
    [Year]                      INT                 NOT NULL,
    [Month]                     INT                 NOT NULL,           -- 1-12
    Status                      NVARCHAR(20)        NOT NULL DEFAULT 'Draft',  -- Draft / Published

    CreatedAtUtc                DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedByObjectId           NVARCHAR(100)       NOT NULL,
    CreatedByDisplayName        NVARCHAR(200)       NOT NULL,

    PublishedAtUtc              DATETIME2           NULL,
    PublishedByObjectId         NVARCHAR(100)       NULL,
    PublishedByDisplayName      NVARCHAR(200)       NULL,

    CONSTRAINT CK_PayrollRun_Status CHECK (Status IN ('Draft', 'Published')),
    CONSTRAINT CK_PayrollRun_Month CHECK ([Month] BETWEEN 1 AND 12),
    CONSTRAINT UQ_PayrollRun_YearMonth UNIQUE ([Year], [Month])
);
GO

CREATE TABLE dbo.PayrollLineItem (
    Id                  UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY DEFAULT NEWID(),
    PayrollRunId        UNIQUEIDENTIFIER    NOT NULL,

    StaffId             UNIQUEIDENTIFIER    NOT NULL,   -- traceability only, not a live FK lookup
    StaffCode           NVARCHAR(20)        NOT NULL,
    DisplayName         NVARCHAR(150)       NOT NULL,
    DisplayOrder        INT                 NOT NULL DEFAULT 0,
    Designation         NVARCHAR(30)        NOT NULL,   -- Teaching / NonTeaching
    BankAccountNumber   NVARCHAR(30)        NOT NULL,
    BankMode            TINYINT             NOT NULL,   -- 1=IOB bulk upload, 2=Manual NEFT
    NetPay              DECIMAL(12, 2)      NOT NULL,

    CONSTRAINT FK_PayrollLineItem_PayrollRun FOREIGN KEY (PayrollRunId)
        REFERENCES dbo.PayrollRun(Id) ON DELETE CASCADE,
    CONSTRAINT CK_PayrollLineItem_Designation CHECK (Designation IN ('Teaching', 'NonTeaching')),
    CONSTRAINT CK_PayrollLineItem_BankMode CHECK (BankMode IN (1, 2))
);

CREATE INDEX IX_PayrollLineItem_RunId ON dbo.PayrollLineItem (PayrollRunId);
GO

PRINT 'Payroll module schema created successfully.';
