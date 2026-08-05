-- =============================================================================
-- Shanthi Nikethan Employee Management — Sub-Designation Options
-- =============================================================================
-- Prerequisite: run 02-StaffProfile-Schema.sql first.
--
-- Designation itself is now just two statutory categories: Teaching / NonTeaching
-- (matching the two IOB salary CSV files). This table holds the finer-grained
-- role list ("Office Admin", "Driver", "Cleaner", "Aaya", etc.) shown as a
-- dropdown against each category. Admins can add new entries directly from
-- the Staff Profile screen — no code change or redeploy needed. This script
-- just seeds sensible starting values.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement;
GO

IF OBJECT_ID('dbo.SubDesignationOption', 'U') IS NOT NULL DROP TABLE dbo.SubDesignationOption;
GO

CREATE TABLE dbo.SubDesignationOption (
    Id              INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
    Category        NVARCHAR(30)       NOT NULL,       -- Teaching / NonTeaching
    Name            NVARCHAR(50)       NOT NULL,
    DisplayOrder    INT                NOT NULL DEFAULT 0,
    IsActive        BIT                NOT NULL DEFAULT 1,

    CONSTRAINT CK_SubDesignationOption_Category CHECK (Category IN ('Teaching', 'NonTeaching')),
    CONSTRAINT UQ_SubDesignationOption_CategoryName UNIQUE (Category, Name)
);
GO

INSERT INTO dbo.SubDesignationOption (Category, Name, DisplayOrder) VALUES
    ('Teaching', 'Principal',      1),
    ('Teaching', 'Coordinator',    2),
    ('Teaching', 'Office Staff',   3),
    ('NonTeaching', 'Office Admin', 1),
    ('NonTeaching', 'Driver',       2),
    ('NonTeaching', 'Cleaner',      3),
    ('NonTeaching', 'Aaya',         4);
GO

PRINT 'Sub-designation options seeded.';
PRINT 'Next (optional): run 99-Seed-July-2026.sql to load your 69 staff.';
