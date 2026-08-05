using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;

namespace ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Services;

public interface IStaffProfileService
{
    Task<List<Staff>> ListActiveAsync(string? searchTerm = null, StaffDesignation? designation = null, bool? isEpfEnabled = null, bool? isEsicEnabled = null, CancellationToken ct = default);
    Task<List<Staff>> ListSoftDeletedAsync(CancellationToken ct = default);
    Task<Staff?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<Staff> CreateAsync(Staff staff, CancellationToken ct = default);
    Task<Staff> UpdateAsync(Staff staff, CancellationToken ct = default);
    Task SoftDeleteAsync(Guid id, string? reason, CancellationToken ct = default);
    Task SoftDeleteManyAsync(IEnumerable<Guid> ids, string? reason, CancellationToken ct = default);
    Task<bool> ReactivateAsync(Guid id, CancellationToken ct = default);
    Task<int> PurgeExpiredAsync(int retentionDays, CancellationToken ct = default);
}

public class StaffProfileService : IStaffProfileService
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public StaffProfileService(IDbContextFactory<AppDbContext> dbf, ICurrentUser user, IAuditService audit)
    {
        _dbf = dbf;
        _user = user;
        _audit = audit;
    }

    public async Task<List<Staff>> ListActiveAsync(string? searchTerm = null, StaffDesignation? designation = null, bool? isEpfEnabled = null, bool? isEsicEnabled = null, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var q = db.Set<Staff>().AsNoTracking().Where(s => s.SoftDeletedAtUtc == null);

        if (designation.HasValue)
            q = q.Where(s => s.Designation == designation.Value);

        if (isEpfEnabled.HasValue)
        {
            q = isEpfEnabled.Value
                ? q.Where(s => s.EpfUan != null && s.EpfUan != "")
                : q.Where(s => s.EpfUan == null || s.EpfUan == "");
        }

        if (isEsicEnabled.HasValue)
        {
            q = isEsicEnabled.Value
                ? q.Where(s => s.EsicNumber != null && s.EsicNumber != "")
                : q.Where(s => s.EsicNumber == null || s.EsicNumber == "");
        }

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var t = searchTerm.Trim();
            q = q.Where(s => EF.Functions.Like(s.DisplayName, $"%{t}%")
                          || EF.Functions.Like(s.BankAccountNumber, $"%{t}%")
                          || EF.Functions.Like(s.PhoneNumber ?? "", $"%{t}%")
                          || EF.Functions.Like(s.EmailAddress ?? "", $"%{t}%"));
        }

        return await q.OrderBy(s => s.DisplayOrder).ThenBy(s => s.DisplayName).ToListAsync(ct);
    }

    public async Task<List<Staff>> ListSoftDeletedAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<Staff>().AsNoTracking()
            .Where(s => s.SoftDeletedAtUtc != null)
            .OrderByDescending(s => s.SoftDeletedAtUtc)
            .ToListAsync(ct);
    }

    public async Task<Staff?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<Staff>().AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<Staff> CreateAsync(Staff staff, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        if (await db.Set<Staff>().AnyAsync(s => s.BankAccountNumber == staff.BankAccountNumber, ct))
            throw new InvalidOperationException($"A staff member with account number {staff.BankAccountNumber} already exists.");

        // Auto-generate StaffCode if not provided; otherwise validate the custom one
        if (string.IsNullOrWhiteSpace(staff.StaffCode))
        {
            staff.StaffCode = await GenerateStaffCodeAsync(db, staff.Designation, ct);
        }
        else
        {
            staff.StaffCode = staff.StaffCode.Trim();
            ValidateStaffCodeFormat(staff.StaffCode);
            if (await db.Set<Staff>().AnyAsync(s => s.StaffCode == staff.StaffCode, ct))
                throw new InvalidOperationException($"Staff code '{staff.StaffCode}' is already in use.");
        }

        // Auto-generate DisplayOrder if not provided
        if (staff.DisplayOrder == 0)
        {
            var maxOrder = await db.Set<Staff>().MaxAsync(s => (int?)s.DisplayOrder, ct) ?? 0;
            staff.DisplayOrder = maxOrder + 1;
        }

        staff.CreatedAtUtc = DateTime.UtcNow;
        staff.CreatedByObjectId = _user.ObjectId;
        staff.CreatedByDisplayName = _user.DisplayName;
        staff.IsActive = true;

        db.Set<Staff>().Add(staff);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("StaffProfile", "Staff", staff.Id.ToString(), "Create",
            newValue: staff.DisplayName, context: $"Designation: {staff.Designation}, Gross: {staff.GrossPay}", ct: ct);

        return staff;
    }

    public async Task<Staff> UpdateAsync(Staff staff, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var existing = await db.Set<Staff>().FirstOrDefaultAsync(s => s.Id == staff.Id, ct)
            ?? throw new InvalidOperationException($"Staff {staff.Id} not found.");

        // Validate custom Staff Code changes (format + uniqueness) before anything else
        var newStaffCode = (staff.StaffCode ?? "").Trim();
        if (existing.StaffCode != newStaffCode)
        {
            ValidateStaffCodeFormat(newStaffCode);
            if (await db.Set<Staff>().AnyAsync(s => s.StaffCode == newStaffCode && s.Id != staff.Id, ct))
                throw new InvalidOperationException($"Staff code '{newStaffCode}' is already in use by another staff member.");
        }

        // Track significant field changes for audit
        var changes = new List<(string field, string? oldV, string? newV)>();
        if (existing.StaffCode != newStaffCode) changes.Add(("StaffCode", existing.StaffCode, newStaffCode));
        if (existing.DisplayName != staff.DisplayName) changes.Add(("DisplayName", existing.DisplayName, staff.DisplayName));
        if (existing.PhoneNumber != staff.PhoneNumber) changes.Add(("PhoneNumber", existing.PhoneNumber, staff.PhoneNumber));
        if (existing.BankAccountNumber != staff.BankAccountNumber) changes.Add(("BankAccountNumber", existing.BankAccountNumber, staff.BankAccountNumber));
        if (existing.GrossPay != staff.GrossPay) changes.Add(("GrossPay", existing.GrossPay.ToString("0.00"), staff.GrossPay.ToString("0.00")));
        if (existing.Designation != staff.Designation) changes.Add(("Designation", existing.Designation.ToString(), staff.Designation.ToString()));
        if (existing.NetPayOverride != staff.NetPayOverride) changes.Add(("NetPayOverride", existing.NetPayOverride?.ToString("0.00"), staff.NetPayOverride?.ToString("0.00")));
        if (existing.EpfUan != staff.EpfUan) changes.Add(("EpfUan", existing.EpfUan, staff.EpfUan));
        if (existing.EsicNumber != staff.EsicNumber) changes.Add(("EsicNumber", existing.EsicNumber, staff.EsicNumber));

        // Copy scalar fields from input to tracked entity
        existing.StaffCode = newStaffCode;
        existing.FirstName = staff.FirstName;
        existing.Initial = staff.Initial;
        existing.DisplayName = staff.DisplayName;
        existing.EmailAddress = staff.EmailAddress;
        existing.PhoneNumber = staff.PhoneNumber;
        existing.AlternatePhoneNumber = staff.AlternatePhoneNumber;
        existing.WhatsappNumber = staff.WhatsappNumber;
        existing.CompleteAddress = staff.CompleteAddress;
        existing.BusNumber = staff.BusNumber;
        existing.Designation = staff.Designation;
        existing.SubDesignation = staff.SubDesignation;
        existing.DateOfJoining = staff.DateOfJoining;
        existing.PanNumber = staff.PanNumber;
        existing.AadhaarNumber = staff.AadhaarNumber;
        existing.EpfUan = staff.EpfUan;
        existing.EpfPassword = staff.EpfPassword;
        existing.EsicNumber = staff.EsicNumber;
        existing.BankAccountNumber = staff.BankAccountNumber;
        existing.BankIfscCode = staff.BankIfscCode;
        existing.BankMode = staff.BankMode;
        existing.GrossPay = staff.GrossPay;
        existing.NetPayOverride = staff.NetPayOverride;
        existing.PhotoRelativePath = staff.PhotoRelativePath;
        existing.BankPassbookRelativePath = staff.BankPassbookRelativePath;
        existing.LastModifiedAtUtc = DateTime.UtcNow;
        existing.LastModifiedByObjectId = _user.ObjectId;
        existing.LastModifiedByDisplayName = _user.DisplayName;

        await db.SaveChangesAsync(ct);

        foreach (var (field, oldV, newV) in changes)
        {
            await _audit.LogAsync("StaffProfile", "Staff", existing.Id.ToString(), "Update",
                field: field, oldValue: oldV, newValue: newV, ct: ct);
        }

        return existing;
    }

    public async Task SoftDeleteAsync(Guid id, string? reason, CancellationToken ct = default) =>
        await SoftDeleteManyAsync(new[] { id }, reason, ct);

    public async Task SoftDeleteManyAsync(IEnumerable<Guid> ids, string? reason, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var idList = ids.ToList();
        var staff = await db.Set<Staff>().Where(s => idList.Contains(s.Id) && s.SoftDeletedAtUtc == null).ToListAsync(ct);

        foreach (var s in staff)
        {
            s.SoftDeletedAtUtc = DateTime.UtcNow;
            s.SoftDeleteReason = reason;
            s.IsActive = false;
        }
        await db.SaveChangesAsync(ct);

        foreach (var s in staff)
        {
            await _audit.LogAsync("StaffProfile", "Staff", s.Id.ToString(), "SoftDelete",
                context: $"{s.DisplayName}. Reason: {reason ?? "not specified"}", ct: ct);
        }
    }

    public async Task<bool> ReactivateAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var s = await db.Set<Staff>().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s == null || !s.IsSoftDeleted) return false;

        s.SoftDeletedAtUtc = null;
        s.SoftDeleteReason = null;
        s.IsActive = true;
        s.LastModifiedAtUtc = DateTime.UtcNow;
        s.LastModifiedByObjectId = _user.ObjectId;
        s.LastModifiedByDisplayName = _user.DisplayName;

        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("StaffProfile", "Staff", s.Id.ToString(), "Reactivate",
            context: $"Reactivated {s.DisplayName}", ct: ct);
        return true;
    }

    /// <summary>
    /// Hard-deletes soft-deleted records past the retention window.
    /// Called at startup and (in production) on a daily background timer.
    /// </summary>
    public async Task<int> PurgeExpiredAsync(int retentionDays, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var threshold = DateTime.UtcNow.AddDays(-retentionDays);
        var expired = await db.Set<Staff>()
            .Where(s => s.SoftDeletedAtUtc != null && s.SoftDeletedAtUtc < threshold)
            .ToListAsync(ct);

        if (expired.Count == 0) return 0;

        db.Set<Staff>().RemoveRange(expired);
        await db.SaveChangesAsync(ct);

        foreach (var s in expired)
        {
            await _audit.LogAsync("StaffProfile", "Staff", s.Id.ToString(), "HardDelete",
                context: $"Auto-purge after {retentionDays}-day retention. Was: {s.DisplayName} / {s.BankAccountNumber}", ct: ct);
        }
        return expired.Count;
    }

    // ==================================================================
    // Helpers
    // ==================================================================

    /// <summary>
    /// Staff Code can be customized to match the school's own ID format
    /// (e.g. "EMP-2024-001", "T/021", "STAFF_045"). Allows letters, digits,
    /// and the punctuation marks commonly used in employee ID schemes:
    /// hyphen, underscore, forward slash, period. Nothing else — no spaces,
    /// no quotes, no HTML-relevant characters.
    /// </summary>
    private static void ValidateStaffCodeFormat(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("Staff code cannot be blank.");
        if (code.Length > 20)
            throw new InvalidOperationException("Staff code cannot be longer than 20 characters.");
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, @"^[A-Za-z0-9\-_/.]+$"))
            throw new InvalidOperationException("Staff code can only contain letters, numbers, and the symbols - _ / .");
    }

    private static async Task<string> GenerateStaffCodeAsync(AppDbContext db, StaffDesignation designation, CancellationToken ct)
    {
        var prefix = designation == StaffDesignation.Teaching ? "SNM-T-" : "SNM-N-";
        var existingCodes = await db.Set<Staff>()
            .Where(s => s.StaffCode.StartsWith(prefix))
            .Select(s => s.StaffCode)
            .ToListAsync(ct);
        var maxNum = 0;
        foreach (var code in existingCodes)
        {
            if (int.TryParse(code[prefix.Length..], out var n) && n > maxNum) maxNum = n;
        }
        return $"{prefix}{(maxNum + 1):D3}";
    }
}
