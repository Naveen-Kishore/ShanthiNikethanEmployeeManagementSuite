using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;

namespace ShanthiNikethan.EmployeeManagement.Modules.Leave.Data;

/// <summary>
/// One leave record for one staff member. Deliberately simple by design:
/// no approval workflow (that already happens by phone before the fact),
/// no formal leave-type quotas yet. The point of this record is durability
/// — right now this information exists only as a WhatsApp message that
/// scrolls away; this makes it searchable permanently. The substitute
/// arrangement is captured as free text (matching the school's existing
/// period-by-period WhatsApp format) rather than a structured timetable
/// model, which would be a much bigger undertaking than this needs.
/// </summary>
public class LeaveRecord
{
    public Guid Id { get; set; }

    public Guid StaffId { get; set; }
    // Denormalized for display/history resilience — if a staff member is
    // later renamed or soft-deleted, old leave records still read sensibly.
    public string StaffCode { get; set; } = string.Empty;
    public string StaffDisplayName { get; set; } = string.Empty;
    public StaffDesignation Designation { get; set; }

    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public decimal DaysCount { get; set; }

    public string? Reason { get; set; }
    public string? SubstituteArrangementNotes { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedByObjectId { get; set; } = string.Empty;
    public string CreatedByDisplayName { get; set; } = string.Empty;

    /// <summary>True if this record was created automatically by marking "Leave" in the Attendance module, rather than entered directly here. The sync only ever updates/deletes records it created itself — a manually-entered leave record (even a multi-day one covering this same date) is never touched.</summary>
    public bool IsSyncedFromAttendance { get; set; }
}
