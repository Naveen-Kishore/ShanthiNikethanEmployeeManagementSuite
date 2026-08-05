using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;

namespace ShanthiNikethan.EmployeeManagement.Modules.Attendance.Data;

/// <summary>
/// One staff member's attendance for one calendar day — but matching the
/// physical register exactly, that's actually two markings: Morning and
/// Evening session, tracked separately. A half-day is naturally
/// Present(Morning) + Absent/Leave(Evening) rather than its own status,
/// exactly like the paper register.
///
/// Editing rule: a record for today is freely editable by anyone. A
/// record for a past date is locked in the normal UI — editing it
/// requires the admin-override path (IsAdminOverride = true), which is
/// unrestricted today only because no role system exists yet. Once
/// Admin Console/RBAC ships, that override path is exactly where an
/// Admin-role check belongs.
/// </summary>
public class AttendanceRecord
{
    public Guid Id { get; set; }

    public Guid StaffId { get; set; }
    // Denormalized for display/history resilience, matching the Leave module's pattern.
    public string StaffCode { get; set; } = string.Empty;
    public string StaffDisplayName { get; set; } = string.Empty;
    public StaffDesignation Designation { get; set; }

    public DateOnly AttendanceDate { get; set; }
    public AttendanceStatus MorningStatus { get; set; }
    public AttendanceStatus EveningStatus { get; set; }
    public string? Notes { get; set; }

    /// <summary>True if this row was auto-created because the staff member had an approved Leave record covering this date.</summary>
    public bool IsSystemGenerated { get; set; }

    /// <summary>True if this row was created/edited via the past-date admin override path.</summary>
    public bool IsAdminOverride { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedByObjectId { get; set; } = string.Empty;
    public string CreatedByDisplayName { get; set; } = string.Empty;

    public DateTime? LastModifiedAtUtc { get; set; }
    public string? LastModifiedByObjectId { get; set; }
    public string? LastModifiedByDisplayName { get; set; }

    /// <summary>A day scores 1.0 if both sessions are Present, 0.5 if only one is — matching the register's "No. of days Present" column, which reflects half-days naturally.</summary>
    public decimal PresentDayScore =>
        (MorningStatus == AttendanceStatus.Present ? 0.5m : 0m) +
        (EveningStatus == AttendanceStatus.Present ? 0.5m : 0m);
}

public enum AttendanceStatus
{
    Present,
    Absent,
    CasualLeave,
    Leave
}

