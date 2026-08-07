-- =============================================================================
-- Shanthi Nikethan Employee Management — Leave Module Schema
-- =============================================================================
-- One table, deliberately simple: no approval workflow (that already
-- happens by phone before the fact), no leave-type quotas yet. This just
-- makes what's currently only a WhatsApp message permanently searchable.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement_Dev;
GO

IF OBJECT_ID('dbo.LeaveRecord', 'U') IS NOT NULL DROP TABLE dbo.LeaveRecord;
GO

CREATE TABLE dbo.LeaveRecord (
    Id                          UNIQUEIDENTIFIER   NOT NULL PRIMARY KEY DEFAULT NEWID(),

    StaffId                     UNIQUEIDENTIFIER   NOT NULL,
    StaffCode                   NVARCHAR(20)       NOT NULL,
    StaffDisplayName            NVARCHAR(150)      NOT NULL,
    Designation                 NVARCHAR(30)       NOT NULL,

    StartDate                   DATE               NOT NULL,
    EndDate                     DATE               NOT NULL,
    DaysCount                   DECIMAL(4, 1)      NOT NULL,

    Reason                      NVARCHAR(200)      NULL,
    SubstituteArrangementNotes  NVARCHAR(1000)     NULL,

    CreatedAtUtc                DATETIME2          NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedByObjectId           NVARCHAR(100)      NOT NULL,
    CreatedByDisplayName        NVARCHAR(200)      NOT NULL,

    CONSTRAINT FK_LeaveRecord_Staff FOREIGN KEY (StaffId) REFERENCES dbo.Staff(Id),
    CONSTRAINT CK_LeaveRecord_Dates CHECK (EndDate >= StartDate),
    CONSTRAINT CK_LeaveRecord_Days CHECK (DaysCount > 0)
);

CREATE INDEX IX_LeaveRecord_StaffId ON dbo.LeaveRecord (StaffId);
CREATE INDEX IX_LeaveRecord_StartDate ON dbo.LeaveRecord (StartDate);
GO

PRINT 'Leave module schema created successfully.';
