-- =============================================================================
-- Shanthi Nikethan Employee Management — Add Attendance.ViewOwn permission
-- =============================================================================
-- The original Admin Console foundation script gave Regular Staff
-- self-service access to Payroll and Leave, but missed the equivalent for
-- Attendance. Idempotent — safe to run even if already applied.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement_Dev;
GO

INSERT INTO dbo.RoleGroupPermission (RoleGroupId, PermissionKey)
SELECT g.Id, 'Attendance.ViewOwn'
FROM dbo.RoleGroup g
WHERE g.Name = 'Regular Staff'
  AND NOT EXISTS (
      SELECT 1 FROM dbo.RoleGroupPermission p
      WHERE p.RoleGroupId = g.Id AND p.PermissionKey = 'Attendance.ViewOwn'
  );
GO

PRINT 'Attendance.ViewOwn permission added to Regular Staff.';
