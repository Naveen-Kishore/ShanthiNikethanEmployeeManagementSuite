using System.ComponentModel.DataAnnotations;

namespace ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Data;

/// <summary>
/// A single Global-Admin-defined rule: "when this event happens, add/remove
/// the staff member from this Entra group." RuleName is what Office Admin
/// sees in the Add Staff checklist - EntraGroupObjectId never is.
/// </summary>
public class GroupAutomationRule
{
    public Guid Id { get; set; }
    [MaxLength(100)] public string RuleName { get; set; } = string.Empty;
    [MaxLength(300)] public string? Description { get; set; }
    [MaxLength(100)] public string EntraGroupObjectId { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public int DisplayOrder { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    [MaxLength(100)] public string CreatedByObjectId { get; set; } = string.Empty;
    [MaxLength(200)] public string CreatedByDisplayName { get; set; } = string.Empty;
}

/// <summary>
/// Records which rules were actually applied to which staff member, and
/// when. This is what lets reactivation (within the 30-day Entra recovery
/// window) replay the exact same group memberships automatically, rather
/// than asking the office admin to remember and re-select which rules had
/// been applied originally. RemovedAtUtc null = currently active.
/// </summary>
public class StaffAutomationRuleAssignment
{
    public Guid Id { get; set; }
    public Guid StaffId { get; set; }
    public Guid GroupAutomationRuleId { get; set; }
    public DateTime AppliedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? RemovedAtUtc { get; set; }
}
