namespace ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;

/// <summary>
/// The one identity record that ties everything together: how someone
/// logs in (Entra ID, local credentials, or both), what they're allowed
/// to do (RoleGroupId), and — for self-service — which Staff profile is
/// theirs. A user can have Entra credentials, local credentials, or both
/// (the two local accounts specifically: one full-access fallback for
/// the Global Administrator, one limited fallback for Office Admin).
/// </summary>
public class UserAccount
{
    public Guid Id { get; set; }

    /// <summary>Optional link to a Staff Profile record — set for self-service (Regular Staff) accounts, typically null for the two admin fallback accounts.</summary>
    public Guid? StaffId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    // ---- Entra ID login ----
    public string? EntraObjectId { get; set; }
    public string? EntraUpn { get; set; }

    // ---- Local login (only ever used for the two designated fallback accounts) ----
    public string? LocalUsername { get; set; }
    public string? LocalPasswordHash { get; set; }
    public bool LocalLoginEnabled { get; set; }

    public Guid RoleGroupId { get; set; }
    /// <summary>Optional expiry for this account's current role group assignment. Once past, the account keeps existing (still shows in Members, still authenticates) but has zero effective permissions until an admin extends or reassigns it — a soft lockout, not a full deactivation.</summary>
    public DateTime? RoleGroupExpiresAtUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedByObjectId { get; set; } = string.Empty;
    public string CreatedByDisplayName { get; set; } = string.Empty;

    public DateTime? LastLoginAtUtc { get; set; }
}
