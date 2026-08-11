-- =============================================================================
-- Stage 1 foundation: Group Automation Rules + Dashboard Notifications
-- =============================================================================
-- Pure app-database work - no Entra/Graph calls happen anywhere in this
-- script or the code that uses it yet. That's Stage 2. This just gives
-- Global Admin somewhere to define rules, and gives the future onboarding/
-- offboarding flow somewhere to record what it did and notify Correspondent.
-- =============================================================================
USE ShanthiNikethanEmployeeManagement_DEV;  -- run again against _Prod with this line changed
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'GroupAutomationRule')
BEGIN
    CREATE TABLE dbo.GroupAutomationRule (
        Id                  UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        RuleName            NVARCHAR(100)       NOT NULL,           -- shown to Office Admin, e.g. "Assign License"
        Description         NVARCHAR(300)       NULL,               -- Global Admin's own notes, not shown to Office Admin
        EntraGroupObjectId  NVARCHAR(100)       NOT NULL,           -- the actual Entra group GUID
        IsEnabled           BIT                 NOT NULL DEFAULT 1,
        DisplayOrder        INT                 NOT NULL DEFAULT 0,
        CreatedAtUtc        DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedByObjectId   NVARCHAR(100)       NOT NULL,
        CreatedByDisplayName NVARCHAR(200)      NOT NULL
    );
    CREATE UNIQUE INDEX IX_GroupAutomationRule_RuleName ON dbo.GroupAutomationRule (RuleName);
    PRINT 'Created dbo.GroupAutomationRule.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'StaffAutomationRuleAssignment')
BEGIN
    -- This table is what makes reactivation able to replay the exact same
    -- group memberships automatically, without asking the office admin to
    -- remember and re-select anything - offboarding sets RemovedAtUtc,
    -- reactivation reads back every row where it's still null.
    CREATE TABLE dbo.StaffAutomationRuleAssignment (
        Id                      UNIQUEIDENTIFIER    NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        StaffId                 UNIQUEIDENTIFIER    NOT NULL,
        GroupAutomationRuleId   UNIQUEIDENTIFIER    NOT NULL,
        AppliedAtUtc            DATETIME2           NOT NULL DEFAULT SYSUTCDATETIME(),
        RemovedAtUtc            DATETIME2           NULL,
        CONSTRAINT FK_StaffAutomationRuleAssignment_Staff
            FOREIGN KEY (StaffId) REFERENCES dbo.Staff(Id),
        CONSTRAINT FK_StaffAutomationRuleAssignment_Rule
            FOREIGN KEY (GroupAutomationRuleId) REFERENCES dbo.GroupAutomationRule(Id)
    );
    CREATE INDEX IX_StaffAutomationRuleAssignment_Staff ON dbo.StaffAutomationRuleAssignment (StaffId, RemovedAtUtc);
    PRINT 'Created dbo.StaffAutomationRuleAssignment.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'DashboardNotification')
BEGIN
    CREATE TABLE dbo.DashboardNotification (
        Id                    UNIQUEIDENTIFIER  NOT NULL PRIMARY KEY DEFAULT NEWSEQUENTIALID(),
        Message               NVARCHAR(300)     NOT NULL,
        LinkUrl               NVARCHAR(300)     NULL,               -- e.g. "/staff-profile?open=<staffId>&tab=salary"
        TargetRoleGroupName   NVARCHAR(100)     NOT NULL,           -- e.g. "Correspondent" - matches RoleGroup.Name, not FK'd since role groups can be renamed independently
        CreatedAtUtc          DATETIME2         NOT NULL DEFAULT SYSUTCDATETIME(),
        CreatedByObjectId     NVARCHAR(100)     NOT NULL,
        CreatedByDisplayName  NVARCHAR(200)     NOT NULL,
        ExpiresAtUtc          DATETIME2         NULL                -- optional auto-hide, so old notifications don't accumulate forever
    );
    CREATE INDEX IX_DashboardNotification_Target ON dbo.DashboardNotification (TargetRoleGroupName, CreatedAtUtc DESC);
    PRINT 'Created dbo.DashboardNotification.';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'DashboardNotificationDismissal')
BEGIN
    -- Per-user, not per-notification: if Correspondent has two people, each
    -- can dismiss independently without hiding it for the other.
    CREATE TABLE dbo.DashboardNotificationDismissal (
        NotificationId    UNIQUEIDENTIFIER NOT NULL,
        UserAccountId     UNIQUEIDENTIFIER NOT NULL,
        DismissedAtUtc    DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
        CONSTRAINT PK_DashboardNotificationDismissal PRIMARY KEY (NotificationId, UserAccountId),
        CONSTRAINT FK_DashboardNotificationDismissal_Notification
            FOREIGN KEY (NotificationId) REFERENCES dbo.DashboardNotification(Id),
        CONSTRAINT FK_DashboardNotificationDismissal_UserAccount
            FOREIGN KEY (UserAccountId) REFERENCES dbo.UserAccount(Id)
    );
    PRINT 'Created dbo.DashboardNotificationDismissal.';
END
GO

-- ---- Permission seed: Admin.ManageAutomationRules, Global Administrator only ----
DECLARE @GlobalAdminId UNIQUEIDENTIFIER = (SELECT Id FROM dbo.RoleGroup WHERE Name = 'Global Administrator');

IF @GlobalAdminId IS NULL
BEGIN
    PRINT 'ERROR: Global Administrator role group not found - run the Admin Console foundation script first.';
END
ELSE IF NOT EXISTS (
    SELECT 1 FROM dbo.RoleGroupPermission
    WHERE RoleGroupId = @GlobalAdminId AND PermissionKey = 'Admin.ManageAutomationRules'
)
BEGIN
    INSERT INTO dbo.RoleGroupPermission (Id, RoleGroupId, PermissionKey)
    VALUES (NEWID(), @GlobalAdminId, 'Admin.ManageAutomationRules');
    PRINT 'Granted Admin.ManageAutomationRules to Global Administrator.';
END
ELSE
BEGIN
    PRINT 'Admin.ManageAutomationRules already granted to Global Administrator - nothing to do.';
END
GO
