-- =============================================================================
-- Shanthi Nikethan Employee Management — Core Schema
-- =============================================================================
-- Run this once against a fresh SQL Server instance.
-- Creates the database and cross-cutting tables used by every module.
-- =============================================================================

IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'ShanthiNikethanEmployeeManagement')
BEGIN
    CREATE DATABASE ShanthiNikethanEmployeeManagement;
END
GO

USE ShanthiNikethanEmployeeManagement_Dev;
GO

-- =============================================================================
-- AuditLog: every mutation across every module records here.
-- Append-only. Never edited via the UI.
-- =============================================================================
IF OBJECT_ID('dbo.AuditLog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.AuditLog (
        Id                  BIGINT          IDENTITY(1,1) PRIMARY KEY,
        OccurredAtUtc       DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME(),
        ActorDisplayName    NVARCHAR(200)   NOT NULL,       -- Entra display name
        ActorObjectId       NVARCHAR(100)   NOT NULL,       -- Entra oid claim
        Module              NVARCHAR(50)    NOT NULL,       -- "StaffProfile", "Payroll", etc.
        EntityType          NVARCHAR(100)   NOT NULL,       -- "Staff", "SalaryRecord", etc.
        EntityId            NVARCHAR(50)    NULL,           -- string, supports GUID or int
        Action              NVARCHAR(50)    NOT NULL,       -- Create/Update/SoftDelete/Reactivate/Publish/Export
        FieldName           NVARCHAR(100)   NULL,           -- for field-level updates
        OldValue            NVARCHAR(500)   NULL,
        NewValue            NVARCHAR(500)   NULL,
        Context             NVARCHAR(500)   NULL            -- free-text tag
    );

    CREATE INDEX IX_AuditLog_OccurredAt   ON dbo.AuditLog (OccurredAtUtc DESC);
    CREATE INDEX IX_AuditLog_Entity       ON dbo.AuditLog (EntityType, EntityId);
    CREATE INDEX IX_AuditLog_Module       ON dbo.AuditLog (Module, OccurredAtUtc DESC);
END
GO

-- =============================================================================
-- ModuleState: records which modules are enabled at runtime.
-- Populated by the app on startup for observability.
-- =============================================================================
IF OBJECT_ID('dbo.ModuleState', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.ModuleState (
        ModuleName          NVARCHAR(50)    NOT NULL PRIMARY KEY,
        IsEnabled           BIT             NOT NULL,
        LicenseTier         NVARCHAR(20)    NOT NULL,
        LastStartedAtUtc    DATETIME2       NOT NULL DEFAULT SYSUTCDATETIME()
    );
END
GO

-- =============================================================================
-- Helper view: current academic year start (assumes June start).
-- Used by payroll/reporting modules.
-- =============================================================================
IF OBJECT_ID('dbo.vw_CurrentAcademicYear', 'V') IS NOT NULL
    DROP VIEW dbo.vw_CurrentAcademicYear;
GO
CREATE VIEW dbo.vw_CurrentAcademicYear AS
SELECT
    CASE
        WHEN MONTH(SYSUTCDATETIME()) >= 6 THEN YEAR(SYSUTCDATETIME())
        ELSE YEAR(SYSUTCDATETIME()) - 1
    END AS AcademicYearStart;
GO

PRINT 'Core schema created successfully.';
PRINT 'Next: run 02-StaffProfile-Schema.sql';
