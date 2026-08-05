-- =============================================================================
-- Shanthi Nikethan Employee Management — Migration: Designation Restructure
-- =============================================================================
-- Run this ONLY if you already have a running database from an earlier version
-- (i.e. you've already run 02-StaffProfile-Schema.sql once before today).
--
-- This ALTERs your existing Staff table in place — it does NOT drop or
-- recreate it, so all your existing staff records, edits, and soft-deletes
-- are preserved.
--
-- If you're setting up a BRAND NEW database instead, skip this file — just
-- run 01, 02, 03, then 99 in order; the updated 02 already has this built in.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement;
GO

-- Step 1: Add the new SubDesignation column (safe to re-run)
IF NOT EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Staff') AND name = 'SubDesignation'
)
BEGIN
    ALTER TABLE dbo.Staff ADD SubDesignation NVARCHAR(50) NULL;
    PRINT 'Added SubDesignation column.';
END
GO

-- Step 2: Move the old fine-grained Designation values into SubDesignation,
-- and collapse Designation down to just Teaching / NonTeaching.
UPDATE dbo.Staff SET SubDesignation = 'Office Admin', Designation = 'NonTeaching' WHERE Designation = 'Admin';
UPDATE dbo.Staff SET SubDesignation = 'Driver',       Designation = 'NonTeaching' WHERE Designation = 'Driver';
UPDATE dbo.Staff SET SubDesignation = 'Cleaner',      Designation = 'NonTeaching' WHERE Designation = 'Cleaner';
UPDATE dbo.Staff SET SubDesignation = 'Aaya',         Designation = 'NonTeaching' WHERE Designation = 'Aaya';
-- Plain 'NonTeaching' and 'Teaching' rows are already correct and untouched.
GO

-- Step 3: Replace the old CHECK constraint (which allowed Admin/Driver/Cleaner/Aaya)
-- with the new one restricted to just the two categories.
DECLARE @constraintName NVARCHAR(200);
SELECT @constraintName = name FROM sys.check_constraints
WHERE parent_object_id = OBJECT_ID('dbo.Staff') AND definition LIKE '%Designation%';

IF @constraintName IS NOT NULL
BEGIN
    EXEC('ALTER TABLE dbo.Staff DROP CONSTRAINT ' + @constraintName);
END

ALTER TABLE dbo.Staff
    ADD CONSTRAINT CK_Staff_Designation CHECK (Designation IN ('Teaching', 'NonTeaching'));
GO

-- Step 4: Create the SubDesignationOption table (skip if you already ran 03)
IF OBJECT_ID('dbo.SubDesignationOption', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.SubDesignationOption (
        Id              INT IDENTITY(1,1)  NOT NULL PRIMARY KEY,
        Category        NVARCHAR(30)       NOT NULL,
        Name            NVARCHAR(50)       NOT NULL,
        DisplayOrder    INT                NOT NULL DEFAULT 0,
        IsActive        BIT                NOT NULL DEFAULT 1,

        CONSTRAINT CK_SubDesignationOption_Category CHECK (Category IN ('Teaching', 'NonTeaching')),
        CONSTRAINT UQ_SubDesignationOption_CategoryName UNIQUE (Category, Name)
    );

    INSERT INTO dbo.SubDesignationOption (Category, Name, DisplayOrder) VALUES
        ('Teaching', 'Principal',      1),
        ('Teaching', 'Coordinator',    2),
        ('Teaching', 'Office Staff',   3),
        ('NonTeaching', 'Office Admin', 1),
        ('NonTeaching', 'Driver',       2),
        ('NonTeaching', 'Cleaner',      3),
        ('NonTeaching', 'Aaya',         4);

    PRINT 'Created and seeded SubDesignationOption table.';
END
GO

-- Step 5: Verify
SELECT Designation, SubDesignation, COUNT(*) AS TotalStaff
FROM dbo.Staff
WHERE SoftDeletedAtUtc IS NULL
GROUP BY Designation, SubDesignation
ORDER BY Designation, SubDesignation;

PRINT 'Migration complete.';
