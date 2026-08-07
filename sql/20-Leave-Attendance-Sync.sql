-- =============================================================================
-- Shanthi Nikethan Employee Management — Leave/Attendance Bi-Directional Sync
-- =============================================================================
-- Adds a discriminator column so the Attendance module can create/remove
-- single-day Leave records automatically when someone marks "Leave" there,
-- without ever touching a manually-entered leave record (even one that
-- happens to cover the same date as part of a multi-day range).
-- =============================================================================

USE ShanthiNikethanEmployeeManagement_Dev;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.LeaveRecord') AND name = 'IsSyncedFromAttendance')
BEGIN
    ALTER TABLE dbo.LeaveRecord ADD IsSyncedFromAttendance BIT NOT NULL DEFAULT 0;
END
GO

PRINT 'Leave/Attendance sync column added successfully.';
