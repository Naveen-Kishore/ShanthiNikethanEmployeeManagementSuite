-- =============================================================================
-- Shanthi Nikethan Employee Management — Payroll Run Types
-- =============================================================================
-- Adds support for non-salary payroll runs (Performance Incentive, Special
-- Class Allowance, Pongal Bonus, etc.) that exist alongside — not inside —
-- the Regular Salary run for a given month. A bonus run never touches Base
-- Net Pay, so it can't accidentally double-pay salary even when disbursed
-- mid-month.
--
-- Existing rows backfill as RunType = 'RegularSalary', which preserves
-- exactly the behavior you already have for any runs created before this
-- script.
-- =============================================================================

USE ShanthiNikethanEmployeeManagement;
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PayrollRun') AND name = 'RunType')
BEGIN
    ALTER TABLE dbo.PayrollRun ADD RunType NVARCHAR(30) NOT NULL DEFAULT 'RegularSalary';
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('dbo.PayrollRun') AND name = 'OtherLabel')
BEGIN
    ALTER TABLE dbo.PayrollRun ADD OtherLabel NVARCHAR(100) NULL;
END
GO

-- Replace the old (Year, Month) uniqueness with (Year, Month, RunType) —
-- a month can now have a Regular Salary run AND a Pongal Bonus run, but
-- not two Regular Salary runs for the same month (that protection stays).
IF EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_PayrollRun_YearMonth')
BEGIN
    ALTER TABLE dbo.PayrollRun DROP CONSTRAINT UQ_PayrollRun_YearMonth;
END
GO

IF NOT EXISTS (SELECT 1 FROM sys.key_constraints WHERE name = 'UQ_PayrollRun_YearMonthType')
BEGIN
    ALTER TABLE dbo.PayrollRun ADD CONSTRAINT UQ_PayrollRun_YearMonthType UNIQUE ([Year], [Month], RunType);
END
GO

ALTER TABLE dbo.PayrollRun
    ADD CONSTRAINT CK_PayrollRun_RunType CHECK (RunType IN
        ('RegularSalary', 'PerformanceIncentive', 'SpecialClassAllowance', 'PongalBonus', 'Other'));
GO

PRINT 'PayrollRun RunType migration completed successfully.';
