using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Leave.Data;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;

namespace ShanthiNikethan.EmployeeManagement.Modules.Leave.Services;

public interface ILeaveService
{
    Task<List<LeaveRecord>> ListAsync(int? year = null, int? month = null, Guid? staffId = null, CancellationToken ct = default);
    Task<List<LeaveRecord>> ListInRangeAsync(DateOnly rangeStart, DateOnly rangeEnd, CancellationToken ct = default);
    Task<LeaveRecord?> GetAsync(Guid id, CancellationToken ct = default);
    Task<LeaveRecord> CreateAsync(LeaveRecord record, CancellationToken ct = default);
    Task UpdateAsync(LeaveRecord record, CancellationToken ct = default);
    Task DeleteAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// Called by Attendance when someone marks/un-marks "Leave" for a
    /// specific day. dayFraction is 0.5 (one session) or 1.0 (both) —
    /// creates or updates a single-day record for that exact date, tagged
    /// IsSyncedFromAttendance so it's clearly distinct from anything
    /// entered directly in Leave Management. dayFraction of 0 removes the
    /// synced record if one exists. Never touches a manually-entered
    /// record, even one covering the same date as part of a longer range.
    /// </summary>
    Task SyncFromAttendanceAsync(Guid staffId, DateOnly date, decimal dayFraction, CancellationToken ct = default);
}

public class LeaveService : ILeaveService
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public LeaveService(IDbContextFactory<AppDbContext> dbf, ICurrentUser user, IAuditService audit)
    {
        _dbf = dbf;
        _user = user;
        _audit = audit;
    }

    public async Task<List<LeaveRecord>> ListAsync(int? year = null, int? month = null, Guid? staffId = null, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var q = db.Set<LeaveRecord>().AsNoTracking().AsQueryable();

        if (year.HasValue)
            q = q.Where(r => r.StartDate.Year == year.Value || r.EndDate.Year == year.Value);
        if (month.HasValue)
            q = q.Where(r => r.StartDate.Month == month.Value || r.EndDate.Month == month.Value);
        if (staffId.HasValue)
            q = q.Where(r => r.StaffId == staffId.Value);

        return await q.OrderByDescending(r => r.StartDate).ToListAsync(ct);
    }

    public async Task<List<LeaveRecord>> ListInRangeAsync(DateOnly rangeStart, DateOnly rangeEnd, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<LeaveRecord>().AsNoTracking()
            .Where(r => r.StartDate <= rangeEnd && r.EndDate >= rangeStart)
            .OrderBy(r => r.StartDate).ThenBy(r => r.StaffDisplayName)
            .ToListAsync(ct);
    }

    public async Task<LeaveRecord?> GetAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<LeaveRecord>().AsNoTracking().FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<LeaveRecord> CreateAsync(LeaveRecord record, CancellationToken ct = default)
    {
        if (record.EndDate < record.StartDate)
            throw new InvalidOperationException("End date cannot be before start date.");
        if (record.DaysCount <= 0)
            throw new InvalidOperationException("Number of days must be greater than zero.");

        await using var db = await _dbf.CreateDbContextAsync(ct);

        var staff = await db.Set<Staff>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == record.StaffId, ct)
            ?? throw new InvalidOperationException("Staff member not found.");

        record.Id = Guid.NewGuid();
        record.StaffCode = staff.StaffCode;
        record.StaffDisplayName = staff.DisplayName;
        record.Designation = staff.Designation;
        record.CreatedAtUtc = DateTime.UtcNow;
        record.CreatedByObjectId = _user.ObjectId;
        record.CreatedByDisplayName = _user.DisplayName;

        db.Set<LeaveRecord>().Add(record);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Leave", "LeaveRecord", record.Id.ToString(), "Create",
            newValue: $"{record.StaffDisplayName}: {record.DaysCount} day(s) from {record.StartDate:dd MMM yyyy}", ct: ct);

        return record;
    }

    public async Task UpdateAsync(LeaveRecord record, CancellationToken ct = default)
    {
        if (record.EndDate < record.StartDate)
            throw new InvalidOperationException("End date cannot be before start date.");
        if (record.DaysCount <= 0)
            throw new InvalidOperationException("Number of days must be greater than zero.");

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var existing = await db.Set<LeaveRecord>().FirstOrDefaultAsync(r => r.Id == record.Id, ct)
            ?? throw new InvalidOperationException("Leave record not found.");

        existing.StartDate = record.StartDate;
        existing.EndDate = record.EndDate;
        existing.DaysCount = record.DaysCount;
        existing.Reason = record.Reason;
        existing.SubstituteArrangementNotes = record.SubstituteArrangementNotes;

        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Leave", "LeaveRecord", existing.Id.ToString(), "Update",
            context: $"{existing.StaffDisplayName}: {existing.DaysCount} day(s) from {existing.StartDate:dd MMM yyyy}", ct: ct);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var record = await db.Set<LeaveRecord>().FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new InvalidOperationException("Leave record not found.");

        db.Set<LeaveRecord>().Remove(record);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Leave", "LeaveRecord", id.ToString(), "Delete",
            context: $"{record.StaffDisplayName}: {record.DaysCount} day(s) from {record.StartDate:dd MMM yyyy}", ct: ct);
    }

    public async Task SyncFromAttendanceAsync(Guid staffId, DateOnly date, decimal dayFraction, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        // Any record already covering this exact date, synced or manual.
        var existing = await db.Set<LeaveRecord>()
            .FirstOrDefaultAsync(r => r.StaffId == staffId && r.StartDate <= date && r.EndDate >= date, ct);

        if (existing != null && !existing.IsSyncedFromAttendance)
            return; // a manually-entered record already covers this date — never touch it

        if (dayFraction <= 0)
        {
            if (existing != null && existing.IsSyncedFromAttendance && existing.StartDate == date && existing.EndDate == date)
            {
                db.Set<LeaveRecord>().Remove(existing);
                await db.SaveChangesAsync(ct);
                await _audit.LogAsync("Leave", "LeaveRecord", existing.Id.ToString(), "Delete",
                    context: $"Un-marked in Attendance — {existing.StaffDisplayName}, {date:dd MMM yyyy}", ct: ct);
            }
            return;
        }

        if (existing != null)
        {
            // Update the synced record's day count (e.g. half-day -> full-day).
            if (existing.DaysCount == dayFraction) return;
            existing.DaysCount = dayFraction;
            await db.SaveChangesAsync(ct);
            await _audit.LogAsync("Leave", "LeaveRecord", existing.Id.ToString(), "Update",
                context: $"Synced from Attendance — {existing.StaffDisplayName}, {date:dd MMM yyyy}: {dayFraction} day(s)", ct: ct);
            return;
        }

        var staff = await db.Set<Staff>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == staffId, ct);
        if (staff == null) return;

        var record = new LeaveRecord
        {
            Id = Guid.NewGuid(),
            StaffId = staffId,
            StaffCode = staff.StaffCode,
            StaffDisplayName = staff.DisplayName,
            Designation = staff.Designation,
            StartDate = date,
            EndDate = date,
            DaysCount = dayFraction,
            Reason = "Marked via Attendance",
            IsSyncedFromAttendance = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByObjectId = _user.ObjectId,
            CreatedByDisplayName = _user.DisplayName
        };
        db.Set<LeaveRecord>().Add(record);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Leave", "LeaveRecord", record.Id.ToString(), "Create",
            context: $"Synced from Attendance — {record.StaffDisplayName}, {date:dd MMM yyyy}: {dayFraction} day(s)", ct: ct);
    }
}
