using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Payroll.Data;
using ShanthiNikethan.EmployeeManagement.Modules.Payroll.Services;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Leave.Services;

namespace ShanthiNikethan.EmployeeManagement.Modules.Dashboard.Services;

public class PayrollTrendPoint
{
    public string MonthLabel { get; set; } = "";
    public decimal TeachingCost { get; set; }
    public decimal NonTeachingCost { get; set; }
    public decimal TotalCost => TeachingCost + NonTeachingCost;
}

public class DashboardData
{
    public int TotalActiveStaff { get; set; }
    public int TeachingCount { get; set; }
    public int NonTeachingCount { get; set; }
    public int EpfEnrolledCount { get; set; }
    public int EsicEnrolledCount { get; set; }
    public List<(string SubDesignation, int Count)> SubDesignationBreakdown { get; set; } = new();

    public List<PayrollTrendPoint> PayrollTrend { get; set; } = new();
    public decimal? LatestPayrollCost { get; set; }
    public string? LatestPayrollMonth { get; set; }
    public int PublishedRunCount { get; set; }

    public int StaffOnLeaveToday { get; set; }
    public decimal LeaveDaysThisMonth { get; set; }
    public int LeaveRecordsThisMonth { get; set; }

    public List<AuditLogEntry> RecentActivity { get; set; } = new();
}

public interface IDashboardService
{
    Task<DashboardData> GetDashboardDataAsync(CancellationToken ct = default);
}

public class DashboardService : IDashboardService
{
    private readonly IStaffProfileService _staffService;
    private readonly IPayrollService _payrollService;
    private readonly ILeaveService _leaveService;
    private readonly IAuditService _auditService;

    public DashboardService(IStaffProfileService staffService, IPayrollService payrollService,
        ILeaveService leaveService, IAuditService auditService)
    {
        _staffService = staffService;
        _payrollService = payrollService;
        _leaveService = leaveService;
        _auditService = auditService;
    }

    public async Task<DashboardData> GetDashboardDataAsync(CancellationToken ct = default)
    {
        var data = new DashboardData();

        var activeStaff = await _staffService.ListActiveAsync(ct: ct);
        data.TotalActiveStaff = activeStaff.Count;
        data.TeachingCount = activeStaff.Count(s => s.Designation == StaffDesignation.Teaching);
        data.NonTeachingCount = activeStaff.Count(s => s.Designation == StaffDesignation.NonTeaching);
        data.EpfEnrolledCount = activeStaff.Count(s => s.IsEpfEnabled);
        data.EsicEnrolledCount = activeStaff.Count(s => s.IsEsicEnabled);
        data.SubDesignationBreakdown = activeStaff
            .Where(s => !string.IsNullOrEmpty(s.SubDesignation))
            .GroupBy(s => s.SubDesignation!)
            .Select(g => (g.Key, g.Count()))
            .OrderByDescending(x => x.Item2)
            .Take(8)
            .ToList();

        var allRuns = await _payrollService.ListRunsAsync(ct);
        var publishedSalaryRuns = allRuns
            .Where(r => r.Status == PayrollRunStatus.Published && r.IsRegularSalary)
            .OrderBy(r => r.Year).ThenBy(r => r.Month)
            .ToList();
        data.PublishedRunCount = allRuns.Count(r => r.Status == PayrollRunStatus.Published);

        foreach (var run in publishedSalaryRuns)
        {
            var items = await _payrollService.GetLineItemsAsync(run.Id, ct);
            data.PayrollTrend.Add(new PayrollTrendPoint
            {
                MonthLabel = run.MonthLabel,
                TeachingCost = items.Where(i => i.Designation == StaffDesignation.Teaching).Sum(i => i.NetPay),
                NonTeachingCost = items.Where(i => i.Designation == StaffDesignation.NonTeaching).Sum(i => i.NetPay)
            });
        }

        if (data.PayrollTrend.Count > 0)
        {
            var latest = data.PayrollTrend[^1];
            data.LatestPayrollCost = latest.TotalCost;
            data.LatestPayrollMonth = latest.MonthLabel;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var monthRecords = await _leaveService.ListInRangeAsync(monthStart, monthEnd, ct);
        data.LeaveDaysThisMonth = monthRecords.Sum(r => r.DaysCount);
        data.LeaveRecordsThisMonth = monthRecords.Count;
        data.StaffOnLeaveToday = monthRecords.Count(r => today >= r.StartDate && today <= r.EndDate);

        data.RecentActivity = await _auditService.GetRecentAsync(10, ct);

        return data;
    }
}
