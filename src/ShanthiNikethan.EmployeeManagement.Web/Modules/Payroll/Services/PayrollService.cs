using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Payroll.Data;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Services;

namespace ShanthiNikethan.EmployeeManagement.Modules.Payroll.Services;

public interface IPayrollService
{
    Task<List<PayrollRun>> ListRunsAsync(CancellationToken ct = default);
    Task<PayrollRun?> GetRunAsync(Guid id, CancellationToken ct = default);
    Task<List<PayrollLineItem>> GetLineItemsAsync(Guid runId, CancellationToken ct = default);
    Task<PayrollRun> CreateDraftAsync(int year, int month, PayrollRunType runType, string? otherLabel = null, CancellationToken ct = default);
    Task PublishAsync(Guid runId, CancellationToken ct = default);
    Task DeleteDraftAsync(Guid runId, CancellationToken ct = default);
    Task UpdateLineItemAmountAsync(Guid lineItemId, decimal newAmount, CancellationToken ct = default);
    Task RemoveLineItemsAsync(Guid runId, List<Guid> lineItemIds, CancellationToken ct = default);
}

public class PayrollService : IPayrollService
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly StatutorySalaryCalculator _calculator;

    public PayrollService(IDbContextFactory<AppDbContext> dbf, ICurrentUser user, IAuditService audit, StatutorySalaryCalculator calculator)
    {
        _dbf = dbf;
        _user = user;
        _audit = audit;
        _calculator = calculator;
    }

    public async Task<List<PayrollRun>> ListRunsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<PayrollRun>().AsNoTracking()
            .OrderByDescending(r => r.Year).ThenByDescending(r => r.Month)
            .ToListAsync(ct);
    }

    public async Task<PayrollRun?> GetRunAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<PayrollRun>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<List<PayrollLineItem>> GetLineItemsAsync(Guid runId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<PayrollLineItem>().AsNoTracking()
            .Where(li => li.PayrollRunId == runId)
            .OrderBy(li => li.DisplayOrder)
            .ToListAsync(ct);
    }

    public async Task<PayrollRun> CreateDraftAsync(int year, int month, PayrollRunType runType, string? otherLabel = null, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        if (await db.Set<PayrollRun>().AnyAsync(r => r.Year == year && r.Month == month && r.RunType == runType, ct))
            throw new InvalidOperationException($"A {DescribeRunType(runType, otherLabel)} run for {new DateOnly(year, month, 1):MMMM yyyy} already exists.");

        var activeStaff = await db.Set<Staff>()
            .Where(s => s.SoftDeletedAtUtc == null)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(ct);

        var run = new PayrollRun
        {
            Id = Guid.NewGuid(),
            Year = year,
            Month = month,
            Status = PayrollRunStatus.Draft,
            RunType = runType,
            OtherLabel = runType == PayrollRunType.Other ? otherLabel : null,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByObjectId = _user.ObjectId,
            CreatedByDisplayName = _user.DisplayName
        };
        db.Set<PayrollRun>().Add(run);

        // Regular Salary runs snapshot each person's current Net Pay.
        // Every other run type (incentive, allowance, bonus) starts every
        // active staff member at zero — admin then edits in the amounts
        // for actual recipients and removes everyone else from the draft.
        // This guarantees a bonus run can never accidentally duplicate a
        // salary payment, since Base Net Pay never enters the picture.
        foreach (var s in activeStaff)
        {
            var netPay = runType == PayrollRunType.RegularSalary
                ? s.NetPayOverride ?? _calculator.Compute(s.GrossPay, s.IsEpfEnabled, s.IsEsicEnabled).NetPay
                : 0m;

            db.Set<PayrollLineItem>().Add(new PayrollLineItem
            {
                Id = Guid.NewGuid(),
                PayrollRunId = run.Id,
                StaffId = s.Id,
                StaffCode = s.StaffCode,
                DisplayName = s.DisplayName,
                DisplayOrder = s.DisplayOrder,
                Designation = s.Designation,
                BankAccountNumber = s.BankAccountNumber,
                BankMode = s.BankMode,
                NetPay = netPay
            });
        }

        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Payroll", "PayrollRun", run.Id.ToString(), "Create",
            newValue: $"{run.MonthLabel} ({run.RunTypeLabel})", context: $"Snapshotted {activeStaff.Count} active staff", ct: ct);

        return run;
    }

    private static string DescribeRunType(PayrollRunType type, string? otherLabel) => type switch
    {
        PayrollRunType.RegularSalary => "Regular Salary",
        PayrollRunType.PerformanceIncentive => "Performance Incentive",
        PayrollRunType.SpecialClassAllowance => "Special Class Allowance",
        PayrollRunType.PongalBonus => "Pongal Bonus",
        PayrollRunType.Other => string.IsNullOrWhiteSpace(otherLabel) ? "custom" : otherLabel,
        _ => "payroll"
    };

    public async Task PublishAsync(Guid runId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var run = await db.Set<PayrollRun>().FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new InvalidOperationException("Payroll run not found.");

        if (run.Status == PayrollRunStatus.Published)
            throw new InvalidOperationException("This payroll run is already published.");

        run.Status = PayrollRunStatus.Published;
        run.PublishedAtUtc = DateTime.UtcNow;
        run.PublishedByObjectId = _user.ObjectId;
        run.PublishedByDisplayName = _user.DisplayName;
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Payroll", "PayrollRun", run.Id.ToString(), "Publish",
            context: $"{run.MonthLabel} locked", ct: ct);
    }

    public async Task DeleteDraftAsync(Guid runId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var run = await db.Set<PayrollRun>().FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new InvalidOperationException("Payroll run not found.");

        if (run.Status == PayrollRunStatus.Published)
            throw new InvalidOperationException("Published payroll runs cannot be deleted — they're the historical record.");

        var items = db.Set<PayrollLineItem>().Where(li => li.PayrollRunId == runId);
        db.Set<PayrollLineItem>().RemoveRange(items);
        db.Set<PayrollRun>().Remove(run);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Payroll", "PayrollRun", runId.ToString(), "Delete",
            context: $"Draft {run.MonthLabel} discarded", ct: ct);
    }

    public async Task UpdateLineItemAmountAsync(Guid lineItemId, decimal newAmount, CancellationToken ct = default)
    {
        if (newAmount < 0)
            throw new InvalidOperationException("Amount cannot be negative.");

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var item = await db.Set<PayrollLineItem>().FirstOrDefaultAsync(li => li.Id == lineItemId, ct)
            ?? throw new InvalidOperationException("Line item not found.");

        var run = await db.Set<PayrollRun>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == item.PayrollRunId, ct)
            ?? throw new InvalidOperationException("Payroll run not found.");
        if (run.Status == PayrollRunStatus.Published)
            throw new InvalidOperationException("This run is published and locked — amounts can no longer be changed.");

        var oldAmount = item.NetPay;
        item.NetPay = newAmount;
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Payroll", "PayrollLineItem", item.Id.ToString(), "Update",
            oldValue: oldAmount.ToString("0.00"), newValue: newAmount.ToString("0.00"),
            context: $"{item.DisplayName} — {run.RunTypeLabel} {run.MonthLabel}", ct: ct);
    }

    public async Task RemoveLineItemsAsync(Guid runId, List<Guid> lineItemIds, CancellationToken ct = default)
    {
        if (lineItemIds.Count == 0) return;

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var run = await db.Set<PayrollRun>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == runId, ct)
            ?? throw new InvalidOperationException("Payroll run not found.");
        if (run.Status == PayrollRunStatus.Published)
            throw new InvalidOperationException("This run is published and locked — staff can no longer be removed from it.");

        var items = await db.Set<PayrollLineItem>()
            .Where(li => li.PayrollRunId == runId && lineItemIds.Contains(li.Id))
            .ToListAsync(ct);

        db.Set<PayrollLineItem>().RemoveRange(items);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Payroll", "PayrollRun", runId.ToString(), "RemoveStaff",
            context: $"Removed {items.Count} staff from {run.RunTypeLabel} {run.MonthLabel}: " +
                     string.Join(", ", items.Select(i => i.DisplayName)), ct: ct);
    }
}
