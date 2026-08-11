-- =============================================================================
-- Add sign-in/security context columns to dbo.AuditLog
-- =============================================================================
-- All new columns are nullable - only sign-in/sign-out entries populate most
-- of them (IP, geo-location, device, browser, request ID, sign-in status/
-- error). RoleGroupAtTime is populated for every entry, not just sign-ins -
-- it's genuinely useful everywhere ("did a Regular Staff account somehow
-- perform an Admin action?").
--
-- Idempotent: checks each column's existence before adding it, safe to
-- re-run.
-- =============================================================================
USE ShanthiNikethanEmployeeManagement_DEV;  -- run again against _Prod with this line changed
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditLog') AND name = 'RequestId')
    ALTER TABLE dbo.AuditLog ADD RequestId NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditLog') AND name = 'RoleGroupAtTime')
    ALTER TABLE dbo.AuditLog ADD RoleGroupAtTime NVARCHAR(100) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditLog') AND name = 'IsSuccess')
    ALTER TABLE dbo.AuditLog ADD IsSuccess BIT NOT NULL DEFAULT 1;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditLog') AND name = 'SignInError')
    ALTER TABLE dbo.AuditLog ADD SignInError NVARCHAR(300) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditLog') AND name = 'Provider')
    ALTER TABLE dbo.AuditLog ADD Provider NVARCHAR(50) NULL;  -- "Entra ID" or "Local Fallback"

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditLog') AND name = 'IpAddress')
    ALTER TABLE dbo.AuditLog ADD IpAddress NVARCHAR(64) NULL;

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditLog') AND name = 'GeoLocation')
    ALTER TABLE dbo.AuditLog ADD GeoLocation NVARCHAR(150) NULL;  -- "Chennai, IN" style, best-effort

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditLog') AND name = 'DeviceInfo')
    ALTER TABLE dbo.AuditLog ADD DeviceInfo NVARCHAR(150) NULL;  -- "Windows PC", "iPhone", etc.

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.AuditLog') AND name = 'BrowserInfo')
    ALTER TABLE dbo.AuditLog ADD BrowserInfo NVARCHAR(150) NULL;  -- "Chrome 128", "Edge 127", etc.

PRINT 'AuditLog sign-in detail columns present (added if missing).';
GO
