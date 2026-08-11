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

    // ---- Brute-force protection for local login ----
    // Both reset to 0/null on any successful login. FailedLoginAttempts
    // increments on each wrong-password attempt against this account;
    // once it crosses the threshold, LockoutEndUtc is set and further
    // attempts are rejected outright until that time passes, without
    // even checking the password - see VerifyLocalLoginAsync.
    public int FailedLoginAttempts { get; set; }
    public DateTime? LockoutEndUtc { get; set; }

    // ---- Archived Entra identity ----
    // Populated only when a Global Admin converts a linked account to local
    // login (EnableLocalLoginOverrideAsync) - preserves the values that get
    // cleared from EntraObjectId/EntraUpn above, so reverting back to Entra
    // later doesn't mean starting from scratch. The revert path (RevertToEntraAsync)
    // still verifies the archived Object ID against Entra directly before restoring
    // it, rather than trusting it blindly - the actual Entra account is never
    // touched by the conversion itself, but it could still have been deleted or
    // renamed independently in the meantime by someone working directly in Entra.
    public string? ArchivedEntraObjectId { get; set; }
    public string? ArchivedEntraUpn { get; set; }

    public Guid RoleGroupId { get; set; }
    /// <summary>Optional expiry for this account's current role group assignment. Once past, the account keeps existing (still shows in Members, still authenticates) but has zero effective permissions until an admin extends or reassigns it — a soft lockout, not a full deactivation.</summary>
    public DateTime? RoleGroupExpiresAtUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedByObjectId { get; set; } = string.Empty;
    public string CreatedByDisplayName { get; set; } = string.Empty;

    public DateTime? LastLoginAtUtc { get; set; }
}
