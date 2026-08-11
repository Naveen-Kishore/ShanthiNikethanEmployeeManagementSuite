using System.ComponentModel.DataAnnotations;

namespace ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;

/// <summary>
/// Master record for every staff member. See sql/02-StaffProfile-Schema.sql
/// for the corresponding database schema and column-level design notes.
///
/// Design principle: only <see cref="GrossPay"/> and <see cref="NetPayOverride"/>
/// are stored. All statutory calculations are derived at runtime by the
/// <see cref="Services.StatutorySalaryCalculator"/> so that changes to
/// government rules (e.g. the EPS ₹15,000 cap moving) do not require any
/// data migration.
/// </summary>
public class Staff
{
    // === Identity ===
    public Guid Id { get; set; }

    /// <summary>Human-readable code, e.g. "SNM-T-047". Auto-generated on create.</summary>
    [MaxLength(20)] public string StaffCode { get; set; } = string.Empty;

    /// <summary>Controls S.No in payroll outputs. Lower = earlier.</summary>
    public int DisplayOrder { get; set; }

    // === Personal ===
    [Required, MaxLength(100)] public string FirstName { get; set; } = string.Empty;
    [MaxLength(10)]  public string? Initial { get; set; }
    [Required, MaxLength(150)] public string DisplayName { get; set; } = string.Empty;
    [MaxLength(500)] public string? PhotoRelativePath { get; set; }

    // === Contact ===
    [MaxLength(150)] public string? EmailAddress { get; set; }
    [MaxLength(20)]  public string? PhoneNumber { get; set; }
    [MaxLength(20)]  public string? AlternatePhoneNumber { get; set; }
    [MaxLength(20)]  public string? WhatsappNumber { get; set; }
    public string? CompleteAddress { get; set; }
    [MaxLength(20)]  public string? BusNumber { get; set; }

    // === Employment ===
    public StaffDesignation Designation { get; set; } = StaffDesignation.Teaching;

    /// <summary>
    /// Finer-grained role within the Designation category, e.g. "Office Admin",
    /// "Driver", "Cleaner", "Aaya" under NonTeaching. Stored as free text; the
    /// available options are managed via <see cref="Services.ISubDesignationService"/>
    /// so admins can add new ones from the UI without a code change.
    /// </summary>
    [MaxLength(50)] public string? SubDesignation { get; set; }

    /// <summary>Plain username seeding a person's sign-in identity - the basis for both a UPN prefix and a potential local account username, kept consistent between the two. Office Admin can view but never edit this once set; only Global Admin can change it.</summary>
    [MaxLength(100)] public string? Username { get; set; }

    public DateOnly DateOfJoining { get; set; }

    // === Statutory IDs ===
    [MaxLength(15)]  public string? PanNumber { get; set; }
    [MaxLength(20)]  public string? AadhaarNumber { get; set; }
    [MaxLength(20)]  public string? EpfUan { get; set; }
    [MaxLength(200)] public string? EpfPassword { get; set; }
    [MaxLength(20)]  public string? EsicNumber { get; set; }

    // === Banking ===
    [Required, MaxLength(30)] public string BankAccountNumber { get; set; } = string.Empty;
    [MaxLength(15)]  public string? BankIfscCode { get; set; }
    [MaxLength(500)] public string? BankPassbookRelativePath { get; set; }
    public BankPaymentMode BankMode { get; set; } = BankPaymentMode.IobBulkUpload;

    // === Salary base ===
    public decimal GrossPay { get; set; }

    /// <summary>
    /// If set, this overrides the computed Net Pay. Used during transition when
    /// only the agreed monthly amount is known (e.g. imported from CSV) and
    /// Gross Pay hasn't been entered yet.
    /// </summary>
    public decimal? NetPayOverride { get; set; }

    // === Lifecycle ===
    public bool IsActive { get; set; } = true;
    public DateTime? SoftDeletedAtUtc { get; set; }
    [MaxLength(500)] public string? SoftDeleteReason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string CreatedByObjectId { get; set; } = string.Empty;
    [MaxLength(200)] public string CreatedByDisplayName { get; set; } = string.Empty;

    public DateTime? LastModifiedAtUtc { get; set; }
    [MaxLength(100)] public string? LastModifiedByObjectId { get; set; }
    [MaxLength(200)] public string? LastModifiedByDisplayName { get; set; }

    // === Convenience (not persisted) ===
    public string FullDisplayName => string.IsNullOrWhiteSpace(Initial)
        ? DisplayName
        : $"{DisplayName}";

    /// <summary>
    /// EPF enrollment is derived from whether a UAN is on file — no separate
    /// toggle to keep in sync. Fill in EPF UAN on the Statutory tab to enable
    /// EPF/EPS/EDLI calculations; clear it to disable them.
    /// </summary>
    public bool IsEpfEnabled => !string.IsNullOrWhiteSpace(EpfUan);

    /// <summary>
    /// ESIC enrollment is derived the same way, from whether an ESIC number
    /// is on file. Note this does NOT re-check the ₹21,000 wage threshold —
    /// per ESIC rules, contribution continues for the rest of the current
    /// contribution period even if wages rise above the threshold mid-period,
    /// so enrollment is a deliberate admin decision, not an automatic one.
    /// </summary>
    public bool IsEsicEnabled => !string.IsNullOrWhiteSpace(EsicNumber);

    public bool IsSoftDeleted => SoftDeletedAtUtc.HasValue;

    /// <summary>
    /// Days remaining before automatic hard-delete. Only meaningful when soft-deleted.
    /// </summary>
    public int DaysUntilPurge(int retentionDays)
    {
        if (!SoftDeletedAtUtc.HasValue) return int.MaxValue;
        var purgeAt = SoftDeletedAtUtc.Value.AddDays(retentionDays);
        var remaining = (purgeAt - DateTime.UtcNow).TotalDays;
        return remaining < 0 ? 0 : (int)Math.Ceiling(remaining);
    }
}

public enum StaffDesignation
{
    Teaching,
    NonTeaching
}

public enum BankPaymentMode : byte
{
    /// <summary>Paid via IOB bulk CSV upload — appears in payroll CSV output.</summary>
    IobBulkUpload = 1,
    /// <summary>Paid manually via NEFT/RTGS — excluded from IOB CSV, still in XLSX/PDF.</summary>
    ManualNeft = 2
}
