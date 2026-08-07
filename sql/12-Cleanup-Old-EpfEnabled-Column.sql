-- =============================================================================
-- Shanthi Nikethan Employee Management — Clean up leftover IsEpfEnabled column
-- =============================================================================
-- Optional. Only needed if 10-Switch-To-Uan-Driven-Enrollment.sql hit the
-- "DF__Staff__IsEpfEnab..." dependency error when trying to drop the old
-- IsEpfEnabled column. That error is harmless — the column just becomes an
-- unused leftover — but this tidies it up if you'd like.
--
-- The original script's DROP COLUMN failed because SQL Server auto-creates a
-- "default constraint" object for any column with a DEFAULT value, and that
-- constraint must be dropped before the column itself can be. This script
-- does both steps in the right order.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement_Dev;
GO

IF EXISTS (
    SELECT 1 FROM sys.columns
    WHERE object_id = OBJECT_ID('dbo.Staff') AND name = 'IsEpfEnabled'
)
BEGIN
    DECLARE @constraintName NVARCHAR(200);
    SELECT @constraintName = dc.name
    FROM sys.default_constraints dc
    JOIN sys.columns c ON c.object_id = dc.parent_object_id AND c.column_id = dc.parent_column_id
    WHERE dc.parent_object_id = OBJECT_ID('dbo.Staff') AND c.name = 'IsEpfEnabled';

    IF @constraintName IS NOT NULL
    BEGIN
        EXEC('ALTER TABLE dbo.Staff DROP CONSTRAINT ' + @constraintName);
        PRINT 'Dropped default constraint: ' + @constraintName;
    END

    ALTER TABLE dbo.Staff DROP COLUMN IsEpfEnabled;
    PRINT 'Dropped IsEpfEnabled column.';
END
ELSE
BEGIN
    PRINT 'IsEpfEnabled column already gone — nothing to do.';
END
GO
