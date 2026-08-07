-- =============================================================================
-- Shanthi Nikethan Employee Management — Admin Console Foundation (Phase 1)
-- =============================================================================
-- RBAC data model: RoleGroup (a named bundle of permissions, fully
-- admin-editable), RoleGroupPermission (which permissions belong to which
-- group), and UserAccount (works for both Entra ID and local login,
-- optionally linked to a Staff profile for self-service).
--
-- Seeds 3 starting role groups (Global Administrator, Office Admin,
-- Regular Staff) and 2 local fallback accounts. The local accounts have
-- NO PASSWORD yet — proper password hashing needs to happen in C# (uses
-- .NET's PBKDF2 implementation with a random salt per account, which SQL
-- can't replicate), so passwords are set via a one-time bootstrap
-- endpoint after this script runs. See the delivery notes for the exact
-- URL to visit once.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement_Dev;
GO

IF OBJECT_ID('dbo.UserAccount', 'U') IS NOT NULL DROP TABLE dbo.UserAccount;
IF OBJECT_ID('dbo.RoleGroupPermission', 'U') IS NOT NULL DROP TABLE dbo.RoleGroupPermission;
IF OBJECT_ID('dbo.RoleGroup', 'U') IS NOT NULL DROP TABLE dbo.RoleGroup;
GO

CREATE TABLE dbo.RoleGroup (
    Id                      UNIQUEIDENTIFIER   NOT NULL PRIMARY KEY DEFAULT NEWID(),
    Name                    NVARCHAR(100)      NOT NULL,
    Description             NVARCHAR(300)      NULL,
    IsSystemDefined         BIT                NOT NULL DEFAULT 0,
    CreatedAtUtc            DATETIME2          NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedByObjectId       NVARCHAR(100)      NOT NULL DEFAULT 'system',
    CreatedByDisplayName    NVARCHAR(200)      NOT NULL DEFAULT 'System (seed)',
    CONSTRAINT UQ_RoleGroup_Name UNIQUE (Name)
);
GO

CREATE TABLE dbo.RoleGroupPermission (
    Id              UNIQUEIDENTIFIER   NOT NULL PRIMARY KEY DEFAULT NEWID(),
    RoleGroupId     UNIQUEIDENTIFIER   NOT NULL,
    PermissionKey   NVARCHAR(100)      NOT NULL,
    CONSTRAINT FK_RoleGroupPermission_RoleGroup FOREIGN KEY (RoleGroupId) REFERENCES dbo.RoleGroup(Id) ON DELETE CASCADE,
    CONSTRAINT UQ_RoleGroupPermission UNIQUE (RoleGroupId, PermissionKey)
);
GO

CREATE TABLE dbo.UserAccount (
    Id                      UNIQUEIDENTIFIER   NOT NULL PRIMARY KEY DEFAULT NEWID(),
    StaffId                 UNIQUEIDENTIFIER   NULL,
    DisplayName             NVARCHAR(200)      NOT NULL,

    EntraObjectId           NVARCHAR(100)      NULL,
    EntraUpn                NVARCHAR(200)      NULL,

    LocalUsername           NVARCHAR(100)      NULL,
    LocalPasswordHash       NVARCHAR(500)      NULL,
    LocalLoginEnabled       BIT                NOT NULL DEFAULT 0,

    RoleGroupId             UNIQUEIDENTIFIER   NOT NULL,
    IsActive                BIT                NOT NULL DEFAULT 1,

    CreatedAtUtc            DATETIME2          NOT NULL DEFAULT SYSUTCDATETIME(),
    CreatedByObjectId       NVARCHAR(100)      NOT NULL DEFAULT 'system',
    CreatedByDisplayName    NVARCHAR(200)      NOT NULL DEFAULT 'System (seed)',
    LastLoginAtUtc          DATETIME2          NULL,

    CONSTRAINT FK_UserAccount_RoleGroup FOREIGN KEY (RoleGroupId) REFERENCES dbo.RoleGroup(Id),
    CONSTRAINT FK_UserAccount_Staff FOREIGN KEY (StaffId) REFERENCES dbo.Staff(Id) ON DELETE SET NULL
);
CREATE INDEX IX_UserAccount_EntraObjectId ON dbo.UserAccount (EntraObjectId);
CREATE UNIQUE INDEX IX_UserAccount_LocalUsername ON dbo.UserAccount (LocalUsername) WHERE LocalUsername IS NOT NULL;
GO

-- ---- Seed the 3 starting role groups ----
DECLARE @GlobalAdminId UNIQUEIDENTIFIER = NEWID();
DECLARE @OfficeAdminId UNIQUEIDENTIFIER = NEWID();
DECLARE @RegularStaffId UNIQUEIDENTIFIER = NEWID();

INSERT INTO dbo.RoleGroup (Id, Name, Description, IsSystemDefined) VALUES
    (@GlobalAdminId, 'Global Administrator', 'Full access to everything, including all financial data.', 1),
    (@OfficeAdminId, 'Office Admin', 'Full Leave Management and Attendance access; no financial data anywhere.', 1),
    (@RegularStaffId, 'Regular Staff', 'Self-service only — own profile, own payslip, own leave records.', 1);

-- Global Administrator gets every permission in the catalog.
INSERT INTO dbo.RoleGroupPermission (RoleGroupId, PermissionKey)
SELECT @GlobalAdminId, PermissionKey FROM (VALUES
    ('Dashboard.View'), ('Dashboard.ViewFinancials'),
    ('StaffDirectory.View'), ('StaffDirectory.ViewFinancials'), ('StaffDirectory.Edit'),
    ('Payroll.View'), ('Payroll.Manage'),
    ('Leave.View'), ('Leave.Manage'),
    ('Attendance.View'), ('Attendance.Mark'), ('Attendance.AdminOverride'),
    ('Admin.ManageUsers'), ('Admin.ManageRoleGroups')
) AS p(PermissionKey);

-- Office Admin: full Leave/Attendance, Staff Directory without financials, slim Dashboard.
INSERT INTO dbo.RoleGroupPermission (RoleGroupId, PermissionKey)
SELECT @OfficeAdminId, PermissionKey FROM (VALUES
    ('Dashboard.View'),
    ('StaffDirectory.View'), ('StaffDirectory.Edit'),
    ('Leave.View'), ('Leave.Manage'),
    ('Attendance.View'), ('Attendance.Mark')
) AS p(PermissionKey);

-- Regular Staff: self-service only.
INSERT INTO dbo.RoleGroupPermission (RoleGroupId, PermissionKey)
SELECT @RegularStaffId, PermissionKey FROM (VALUES
    ('Payroll.ViewOwnPayslip'),
    ('Leave.ViewOwn')
) AS p(PermissionKey);

-- ---- Seed the 2 local fallback accounts (no password yet — see notes above) ----
INSERT INTO dbo.UserAccount (DisplayName, LocalUsername, LocalLoginEnabled, RoleGroupId, IsActive)
VALUES
    ('Global Admin (Local Fallback)', 'admin', 0, @GlobalAdminId, 1),
    ('Office Admin (Local Fallback)', 'officeadmin', 0, @OfficeAdminId, 1);

PRINT 'Admin Console foundation created. Local accounts need passwords set via the bootstrap endpoint before local login will work.';
GO
