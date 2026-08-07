-- =============================================================================
-- Shanthi Nikethan Employee Management — Attendance Module Schema (v2)
-- =============================================================================
-- Matches the physical "Register of Attendance of Teachers" exactly: one
-- row per staff member per day, but with separate Morning and Evening
-- session status, mirroring the register's M/E row pairs. A half-day is
-- naturally Present(Morning) + Absent/Leave(Evening) rather than its own
-- status.
--
-- If you ran the earlier 18-Attendance-Schema.sql, this replaces that
-- table entirely — the old single-Status column is gone.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement_Dev;
GO

IF OBJECT_ID('dbo.AttendanceRecord', 'U') IS NOT NULL DROP TABLE dbo.AttendanceRecord;
GO

CREATE TABLE dbo.AttendanceRecord (
    Id                      UNIQUEIDENTIFIER   NOT NULL PRIMARY KEY DEFAULT NEWID(),

    StaffId                 UNIQUEIDENTIFIER   NOT NULL,
    StaffCode               NVARCHAR(20)       NOT NULL,
    StaffDisplayName        NVARCHAR(150)      NOT NULL,
    Designation             NVARCHAR(30)       NOT NULL,

    AttendanceDate          DATE               NOT NULL,
    MorningStatus           NVARCHAR(20)       NOT NULL,
    EveningStatus           NVARCHAR(20)       NOT NULL,
    Notes                   NVARCHAR(300)      NULL,

    IsSystemGenerated       BIT                NOT NULL DEFAULT 0,
    IsAdminOverride         BIT                NOT NULL DEFAULT 0,

    CreatedAtUtc            DATETIME2          NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedByObjectId       NVARCHAR(100)      NOT NULL,
    CreatedByDisplayName    NVARCHAR(200)      NOT NULL,

    LastModifiedAtUtc       DATETIME2          NULL,
    LastModifiedByObjectId  NVARCHAR(100)      NULL,
    LastModifiedByDisplayName NVARCHAR(200)    NULL,

    CONSTRAINT FK_AttendanceRecord_Staff FOREIGN KEY (StaffId) REFERENCES dbo.Staff(Id),
    CONSTRAINT CK_AttendanceRecord_MorningStatus CHECK (MorningStatus IN ('Present', 'Absent', 'CasualLeave', 'Leave')),
    CONSTRAINT CK_AttendanceRecord_EveningStatus CHECK (EveningStatus IN ('Present', 'Absent', 'CasualLeave', 'Leave')),
    CONSTRAINT UQ_AttendanceRecord_StaffDate UNIQUE (StaffId, AttendanceDate)
);

CREATE INDEX IX_AttendanceRecord_AttendanceDate ON dbo.AttendanceRecord (AttendanceDate);
GO

PRINT 'Attendance module schema (v2 — Morning/Evening) created successfully.';
