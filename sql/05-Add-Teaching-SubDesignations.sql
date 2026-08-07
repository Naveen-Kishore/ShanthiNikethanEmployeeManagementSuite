-- =============================================================================
-- Shanthi Nikethan Employee Management — Add Teaching Sub-Designations
-- =============================================================================
-- Adds "Principal", "Coordinator", and "Office Staff" as sub-designations
-- under the Teaching category. Safe to run even if some or all of these
-- already exist (e.g. if you'd already added one manually via the
-- "+ Add new..." option in the app) — it only inserts what's missing.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement_Dev;
GO

INSERT INTO dbo.SubDesignationOption (Category, Name, DisplayOrder)
SELECT v.Category, v.Name, v.DisplayOrder
FROM (VALUES
    ('Teaching', 'Principal',    1),
    ('Teaching', 'Coordinator',  2),
    ('Teaching', 'Office Staff', 3)
) AS v(Category, Name, DisplayOrder)
WHERE NOT EXISTS (
    SELECT 1 FROM dbo.SubDesignationOption existing
    WHERE existing.Category = v.Category AND existing.Name = v.Name
);
GO

SELECT Category, Name, DisplayOrder FROM dbo.SubDesignationOption ORDER BY Category, DisplayOrder;

PRINT 'Teaching sub-designations added (or already present).';
