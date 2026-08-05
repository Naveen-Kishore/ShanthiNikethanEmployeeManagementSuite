using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Attendance.Data;
using ShanthiNikethan.EmployeeManagement.Modules.Leave.Services;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;

namespace ShanthiNikethan.EmployeeManagement.Modules.Attendance.Services;

public record DailyTotals(int PresentM, int AbsentM, int CasualLeaveM, int LeaveM,
                           int PresentE, int AbsentE, int CasualLeaveE, int LeaveE);

public record StaffMonthlyTotal(Guid StaffId, string StaffDisplayName, string StaffCode,
                                 int WorkingDays, decimal DaysPresent, decimal CasualLeaveDays, decimal LeaveDays);

public interface IAttendanceService
{
    /// <summary>
    /// Every active staff member's attendance for one day. Anyone with an
    /// approved Leave record covering this date and no existing
    /// attendance row gets one auto-created with both sessions set to
    /// Leave — that's the "auto-fill from Leave" behavior. Returns one
    /// entry per active staff member, always.
    /// </summary>
    Task<List<AttendanceRecord>> GetForDateAsync(DateOnly date, CancellationToken ct = default);

    Task<List<AttendanceRecord>> ListInRangeAsync(DateOnly start, DateOnly end, CancellationToken ct = default);

    /// <summary>
    /// Creates or overwrites both sessions' status for one staff member on
    /// one day. Throws if the date is in the past and isAdminOverride is
    /// false — that's the same-day-only edit lock.
    /// </summary>
    Task MarkAsync(Guid staffId, DateOnly date, AttendanceStatus morning, AttendanceStatus evening, string? notes, bool isAdminOverride, CancellationToken ct = default);

    /// <summary>
    /// Marks every active staff member who doesn't yet have an attendance
    /// row for this date as Present (both sessions) — the daily "most
    /// people showed up as usual" shortcut. Never overwrites an existing
    /// row (Leave, Absent, or anything already marked), so it's safe to
    /// click even after marking a few exceptions individually.
    /// </summary>
    Task<int> MarkAllUnmarkedPresentAsync(DateOnly date, bool isAdminOverride, CancellationToken ct = default);

    /// <summary>Per-status counts for Morning and Evening separately, for one day — matches the register's own daily summary rows (No. of C.L Teachers, etc.), split by session.</summary>
    DailyTotals ComputeDailyTotals(List<AttendanceRecord> recordsForDay);

    /// <summary>Per-teacher totals for a month: working days (calendar days minus Sundays), days present (half-day-aware), CL days, Leave days — matches the register's right-edge summary columns.</summary>
    Task<List<StaffMonthlyTotal>> GetMonthlyTotalsAsync(int year, int month, CancellationToken ct = default);
}

public class AttendanceService : IAttendanceService
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly ILeaveService _leaveService;

    public AttendanceService(IDbContextFactory<AppDbContext> dbf, ICurrentUser user, IAuditService audit, ILeaveService leaveService)
    {
        _dbf = dbf;
        _user = user;
        _audit = audit;
        _leaveService = leaveService;
    }

    public async Task<List<AttendanceRecord>> GetForDateAsync(DateOnly date, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        var activeStaff = await db.Set<Staff>()
            .Where(s => s.SoftDeletedAtUtc == null)
            .OrderBy(s => s.DisplayName)
            .ToListAsync(ct);

        var existing = await db.Set<AttendanceRecord>()
            .Where(a => a.AttendanceDate == date)
            .ToListAsync(ct);
        var existingByStaff = existing.ToDictionary(a => a.StaffId);

        // Auto-fill: anyone with an approved leave record covering this
        // date, who doesn't already have an attendance row, gets both
        // sessions set to Leave automatically.
        var leaveToday = await _leaveService.ListInRangeAsync(date, date, ct);
        var onLeaveStaffIds = leaveToday.Select(l => l.StaffId).ToHashSet();

        var toCreate = new List<AttendanceRecord>();
        foreach (var s in activeStaff)
        {
            if (existingByStaff.ContainsKey(s.Id)) continue;
            if (!onLeaveStaffIds.Contains(s.Id)) continue;

            var record = new AttendanceRecord
            {
                Id = Guid.NewGuid(),
                StaffId = s.Id,
                StaffCode = s.StaffCode,
                StaffDisplayName = s.DisplayName,
                Designation = s.Designation,
                AttendanceDate = date,
                MorningStatus = AttendanceStatus.Leave,
                EveningStatus = AttendanceStatus.Leave,
                IsSystemGenerated = true,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByObjectId = "system",
                CreatedByDisplayName = "Auto-filled from Leave"
            };
            toCreate.Add(record);
            existingByStaff[s.Id] = record;
        }

        if (toCreate.Count > 0)
        {
            db.Set<AttendanceRecord>().AddRange(toCreate);
            await db.SaveChangesAsync(ct);
        }

        // One row per active staff member, always — staff with neither an
        // existing record nor a leave match show up as "not yet marked"
        // (Id stays Guid.Empty, not persisted, until someone marks them).
        return activeStaff
            .Select(s => existingByStaff.TryGetValue(s.Id, out var a) ? a : new AttendanceRecord
            {
                StaffId = s.Id,
                StaffCode = s.StaffCode,
                StaffDisplayName = s.DisplayName,
                Designation = s.Designation,
                AttendanceDate = date
            })
            .OrderBy(a => a.StaffDisplayName)
            .ToList();
    }

    public async Task<List<AttendanceRecord>> ListInRangeAsync(DateOnly start, DateOnly end, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<AttendanceRecord>()
            .Where(a => a.AttendanceDate >= start && a.AttendanceDate <= end)
            .OrderBy(a => a.AttendanceDate).ThenBy(a => a.StaffDisplayName)
            .ToListAsync(ct);
    }

    public async Task MarkAsync(Guid staffId, DateOnly date, AttendanceStatus morning, AttendanceStatus evening, string? notes, bool isAdminOverride, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (date < today && !isAdminOverride)
            throw new InvalidOperationException("This date is locked — only today's attendance can be marked directly. Past days require an admin override.");

        await using var db = await _dbf.CreateDbContextAsync(ct);

        var existing = await db.Set<AttendanceRecord>()
            .FirstOrDefaultAsync(a => a.StaffId == staffId && a.AttendanceDate == date, ct);

        string oldValue = existing == null ? "(none)" : $"M:{existing.MorningStatus} E:{existing.EveningStatus}";

        if (existing == null)
        {
            var staff = await db.Set<Staff>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == staffId, ct)
                ?? throw new InvalidOperationException("Staff member not found.");

            existing = new AttendanceRecord
            {
                Id = Guid.NewGuid(),
                StaffId = staffId,
                StaffCode = staff.StaffCode,
                StaffDisplayName = staff.DisplayName,
                Designation = staff.Designation,
                AttendanceDate = date,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByObjectId = _user.ObjectId,
                CreatedByDisplayName = _user.DisplayName
            };
            db.Set<AttendanceRecord>().Add(existing);
        }
        else
        {
            existing.LastModifiedAtUtc = DateTime.UtcNow;
            existing.LastModifiedByObjectId = _user.ObjectId;
            existing.LastModifiedByDisplayName = _user.DisplayName;
        }

        existing.MorningStatus = morning;
        existing.EveningStatus = evening;
        existing.Notes = string.IsNullOrWhiteSpace(notes) ? null : notes;
        existing.IsSystemGenerated = false; // a human touched it now, even if it started as an auto-fill
        existing.IsAdminOverride = isAdminOverride;

        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Attendance", "AttendanceRecord", existing.Id.ToString(),
            isAdminOverride ? "AdminOverrideMark" : "Mark",
            oldValue: oldValue, newValue: $"M:{morning} E:{evening}",
            context: $"{existing.StaffDisplayName} — {date:dd MMM yyyy}", ct: ct);

        // Bi-directional sync: reflect a Leave marking (full or half-day)
        // back into the Leave module, so both modules show the same
        // picture regardless of which one someone marked it in first.
        var leaveDayFraction =
            (morning == AttendanceStatus.Leave ? 0.5m : 0m) +
            (evening == AttendanceStatus.Leave ? 0.5m : 0m);
        await _leaveService.SyncFromAttendanceAsync(staffId, date, leaveDayFraction, ct);
    }

    public async Task<int> MarkAllUnmarkedPresentAsync(DateOnly date, bool isAdminOverride, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        if (date < today && !isAdminOverride)
            throw new InvalidOperationException("This date is locked — only today's attendance can be marked directly. Past days require an admin override.");

        await using var db = await _dbf.CreateDbContextAsync(ct);

        var activeStaff = await db.Set<Staff>()
            .Where(s => s.SoftDeletedAtUtc == null)
            .ToListAsync(ct);

        var alreadyMarkedIds = await db.Set<AttendanceRecord>()
            .Where(a => a.AttendanceDate == date)
            .Select(a => a.StaffId)
            .ToListAsync(ct);
        var alreadyMarked = alreadyMarkedIds.ToHashSet();

        var toCreate = activeStaff
            .Where(s => !alreadyMarked.Contains(s.Id))
            .Select(s => new AttendanceRecord
            {
                Id = Guid.NewGuid(),
                StaffId = s.Id,
                StaffCode = s.StaffCode,
                StaffDisplayName = s.DisplayName,
                Designation = s.Designation,
                AttendanceDate = date,
                MorningStatus = AttendanceStatus.Present,
                EveningStatus = AttendanceStatus.Present,
                IsAdminOverride = isAdminOverride,
                CreatedAtUtc = DateTime.UtcNow,
                CreatedByObjectId = _user.ObjectId,
                CreatedByDisplayName = _user.DisplayName
            })
            .ToList();

        if (toCreate.Count == 0) return 0;

        db.Set<AttendanceRecord>().AddRange(toCreate);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Attendance", "AttendanceRecord", null,
            isAdminOverride ? "AdminOverrideBulkMarkPresent" : "BulkMarkPresent",
            newValue: $"{toCreate.Count} staff marked Present",
            context: $"{date:dd MMM yyyy}", ct: ct);

        return toCreate.Count;
    }

    public DailyTotals ComputeDailyTotals(List<AttendanceRecord> recordsForDay)
    {
        int Count(Func<AttendanceRecord, AttendanceStatus> selector, AttendanceStatus status) =>
            recordsForDay.Count(r => r.Id != Guid.Empty && selector(r) == status);

        return new DailyTotals(
            PresentM: Count(r => r.MorningStatus, AttendanceStatus.Present),
            AbsentM: Count(r => r.MorningStatus, AttendanceStatus.Absent),
            CasualLeaveM: Count(r => r.MorningStatus, AttendanceStatus.CasualLeave),
            LeaveM: Count(r => r.MorningStatus, AttendanceStatus.Leave),
            PresentE: Count(r => r.EveningStatus, AttendanceStatus.Present),
            AbsentE: Count(r => r.EveningStatus, AttendanceStatus.Absent),
            CasualLeaveE: Count(r => r.EveningStatus, AttendanceStatus.CasualLeave),
            LeaveE: Count(r => r.EveningStatus, AttendanceStatus.Leave)
        );
    }

    public async Task<List<StaffMonthlyTotal>> GetMonthlyTotalsAsync(int year, int month, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        var activeStaff = await db.Set<Staff>()
            .Where(s => s.SoftDeletedAtUtc == null)
            .OrderBy(s => s.DisplayName)
            .ToListAsync(ct);

        var monthStart = new DateOnly(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var records = await db.Set<AttendanceRecord>()
            .Where(a => a.AttendanceDate >= monthStart && a.AttendanceDate <= monthEnd)
            .ToListAsync(ct);

        // Working days = calendar days minus Sundays. Other holidays
        // (specific Saturdays, festival closures) aren't tracked as a
        // distinct concept yet — a deliberate scope decision, not an
        // oversight, since a proper school-calendar/holidays feature is
        // its own piece of work.
        int workingDays = Enumerable.Range(1, monthEnd.Day)
            .Select(d => new DateOnly(year, month, d))
            .Count(d => d.DayOfWeek != DayOfWeek.Sunday);

        var result = new List<StaffMonthlyTotal>();
        foreach (var s in activeStaff)
        {
            var staffRecords = records.Where(r => r.StaffId == s.Id).ToList();
            var daysPresent = staffRecords.Sum(r => r.PresentDayScore);
            var clDays = staffRecords.Sum(r =>
                (r.MorningStatus == AttendanceStatus.CasualLeave ? 0.5m : 0m) +
                (r.EveningStatus == AttendanceStatus.CasualLeave ? 0.5m : 0m));
            var leaveDays = staffRecords.Sum(r =>
                (r.MorningStatus == AttendanceStatus.Leave ? 0.5m : 0m) +
                (r.EveningStatus == AttendanceStatus.Leave ? 0.5m : 0m));

            result.Add(new StaffMonthlyTotal(s.Id, s.DisplayName, s.StaffCode, workingDays, daysPresent, clDays, leaveDays));
        }

        return result;
    }
}
