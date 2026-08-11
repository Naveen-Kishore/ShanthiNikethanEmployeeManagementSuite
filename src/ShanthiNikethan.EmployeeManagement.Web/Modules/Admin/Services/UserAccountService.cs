using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;
using ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Services;

namespace ShanthiNikethan.EmployeeManagement.Modules.Admin.Services;

public enum LocalLoginOutcome { Success, InvalidCredentials, LockedOut }

/// <summary>Result of a local login attempt - Account is only populated on Success. LockoutRemaining is only populated on LockedOut, for showing a useful "try again in N minutes" message.</summary>
public record LocalLoginAttemptResult(LocalLoginOutcome Outcome, UserAccount? Account = null, TimeSpan? LockoutRemaining = null);

public interface IUserAccountService
{
    Task<List<UserAccount>> ListUsersAsync(CancellationToken ct = default);
    Task<UserAccount?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserAccount?> GetByEntraObjectIdAsync(string objectId, CancellationToken ct = default);
    Task<UserAccount?> GetByEntraUpnAsync(string upn, CancellationToken ct = default);

    /// <summary>Unlike GetByEntraUpnAsync, this matches regardless of IsActive - used to catch "this UPN is already on file here" before attempting to create a new Entra account with it, including for a deactivated account that would otherwise give a confusing raw Graph error instead of a clear one.</summary>
    Task<UserAccount?> FindAnyByEntraUpnAsync(string upn, CancellationToken ct = default);

    /// <summary>Checks a plain username (not a full UPN) against BOTH existing LocalUsername values AND the prefix of existing Entra UPNs - catches the case a plain LocalUsername uniqueness check would miss: a new local login colliding with someone else's Entra sign-in identity, or vice versa.</summary>
    Task<UserAccount?> FindAnyAccountByUsernameAsync(string username, CancellationToken ct = default);

    Task<UserAccount?> GetByLocalUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>Called once an account first matched by UPN (not yet by Object ID) successfully signs in — stores the now-known Object ID so future logins can match on it directly, which is more reliable long-term than UPN (which can change if someone's email is renamed).</summary>
    Task BackfillEntraObjectIdAsync(Guid userAccountId, string entraObjectId, CancellationToken ct = default);
    Task<List<string>> GetEffectivePermissionsAsync(Guid roleGroupId, CancellationToken ct = default);

    Task<List<RoleGroup>> ListRoleGroupsAsync(CancellationToken ct = default);
    Task<RoleGroup> CreateRoleGroupAsync(string name, string? description, List<string> permissionKeys, CancellationToken ct = default);
    Task UpdateRoleGroupPermissionsAsync(Guid roleGroupId, List<string> permissionKeys, CancellationToken ct = default);

    Task<UserAccount> CreateUserAccountAsync(UserAccount account, string? localPassword, CancellationToken ct = default);
    Task SetLocalPasswordAsync(Guid userAccountId, string newPassword, CancellationToken ct = default);

    /// <summary>Deactivates an account immediately - it can no longer sign in or be matched during login, regardless of auth method. Used both for routine offboarding and for shutting down an account that shouldn't have been provisioned (e.g. an unrecognized auto-provisioned identity).</summary>
    Task DeactivateUserAccountAsync(Guid userAccountId, CancellationToken ct = default);
    Task ReactivateUserAccountAsync(Guid userAccountId, CancellationToken ct = default);

    /// <summary>Hard delete - removes the account entirely. Used for cleaning up an account that should never have existed (e.g. an unrecognized auto-provisioned identity), as opposed to Deactivate which is for routine offboarding of a real former user.</summary>
    Task DeleteUserAccountAsync(Guid userAccountId, CancellationToken ct = default);

    Task UpdateUserAccountDetailsAsync(Guid userAccountId, string displayName, Guid? staffId, string? entraUpn = null, string? entraObjectId = null, bool localLoginEnabled = false, string? localUsername = null, CancellationToken ct = default);

    /// <summary>Assigns a role group to an account, with an optional expiry. Replaces the old ChangeRoleGroupAsync — same effect, now expiry-aware in one call rather than two.</summary>
    Task AssignRoleGroupAsync(Guid userAccountId, Guid roleGroupId, DateTime? expiresAtUtc, CancellationToken ct = default);

    /// <summary>Updates only DisplayName - called on every Entra login so a placeholder name (e.g. "Bootstrap Administrator") gets replaced with the real one, and so later name changes in Entra stay reflected here too.</summary>
    Task SyncDisplayNameAsync(Guid userAccountId, string displayName, CancellationToken ct = default);

    /// <summary>Updates only LastLoginAtUtc - called once per genuine sign-in from MainLayout, covering both Entra and local-auth accounts in one place rather than each auth path needing its own reminder to do this.</summary>

    Task UpdateLastLoginAsync(Guid userAccountId, CancellationToken ct = default);

    Task<RoleGroup?> GetRoleGroupByIdAsync(Guid roleGroupId, CancellationToken ct = default);

    /// <summary>Global-Admin-only override: converts a linked (Entra-provisioned) account to local login instead, clearing its Entra identity - the two remain mutually exclusive. This is the one path allowed to override the identity lock that otherwise protects a linked account's Entra fields from being hand-edited. Throws if the caller isn't a Global Administrator, regardless of what the UI shows.</summary>
    Task EnableLocalLoginOverrideAsync(Guid userAccountId, string localUsername, string password, CancellationToken ct = default);

    /// <summary>Reverse of EnableLocalLoginOverrideAsync - same Global-Admin-only enforcement.</summary>
    Task DisableLocalLoginOverrideAsync(Guid userAccountId, CancellationToken ct = default);

    /// <summary>Restores a converted account's original Entra identity from the archived values, but only after confirming the Object ID still resolves to a real Entra account - the conversion itself never touched the actual Entra account, but it could have been deleted or renamed independently since. Throws if there's nothing archived, or if the archived Object ID no longer resolves.</summary>
    Task RevertToEntraAsync(Guid userAccountId, CancellationToken ct = default);
    Task UpdateRoleGroupBasicsAsync(Guid roleGroupId, string name, string? description, CancellationToken ct = default);

    /// <summary>Deletes a custom role group. Throws if it's built-in (protected) or if any account is still assigned to it - members must be reassigned first, rather than silently orphaned.</summary>
    Task DeleteRoleGroupAsync(Guid roleGroupId, CancellationToken ct = default);

    /// <summary>Verifies local username/password, enforcing account lockout after repeated failures. Never throws on bad credentials or lockout - either is a normal outcome, not a system error.</summary>
    Task<LocalLoginAttemptResult> VerifyLocalLoginAsync(string username, string password, CancellationToken ct = default);

    /// <summary>
    /// Called on first login by any Entra ID user with no existing
    /// UserAccount at all. Creates one as Global Administrator - but ONLY
    /// for the specific Object ID configured as Authorization:BootstrapGlobalAdminObjectId.
    /// This is a one-time mechanism for getting the first admin into a
    /// fresh install, not general auto-provisioning - since the old static
    /// AllowedUserObjectIds allowlist was removed, this check is now the
    /// only thing standing between an arbitrary Entra sign-in and being
    /// silently granted Global Administrator. Returns null (not an
    /// account) for anyone who isn't that specific configured identity -
    /// the caller in MainLayout already treats null as "redirect to
    /// /access-denied", so this composes correctly with no caller changes.
    /// </summary>
    Task<UserAccount?> EnsureBootstrapAdminAccountAsync(string entraObjectId, string displayName, CancellationToken ct = default);
}

public class UserAccountService : IUserAccountService
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly IGraphProvisioningService _graphService;
    private readonly IConfiguration _config;
    private readonly PasswordHasher<UserAccount> _hasher = new();

    public UserAccountService(IDbContextFactory<AppDbContext> dbf, ICurrentUser user, IAuditService audit, IGraphProvisioningService graphService, IConfiguration config)
    {
        _dbf = dbf;
        _user = user;
        _audit = audit;
        _graphService = graphService;
        _config = config;
    }

    public async Task<List<UserAccount>> ListUsersAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<UserAccount>().AsNoTracking().OrderBy(u => u.DisplayName).ToListAsync(ct);
    }

    public async Task<UserAccount?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<UserAccount>().AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.IsActive, ct);
    }

    public async Task<UserAccount?> GetByEntraObjectIdAsync(string objectId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<UserAccount>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.EntraObjectId == objectId && u.IsActive, ct);
    }

    public async Task<UserAccount?> GetByEntraUpnAsync(string upn, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<UserAccount>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.EntraUpn != null && u.EntraUpn.ToLower() == upn.ToLower() && u.IsActive, ct);
    }

    public async Task<UserAccount?> FindAnyByEntraUpnAsync(string upn, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<UserAccount>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.EntraUpn != null && u.EntraUpn.ToLower() == upn.ToLower(), ct);
    }

    public async Task<UserAccount?> FindAnyAccountByUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var normalized = username.Trim().ToLower();
        var prefixMatch = normalized + "@";
        return await db.Set<UserAccount>().AsNoTracking().FirstOrDefaultAsync(u =>
            (u.LocalUsername != null && u.LocalUsername.ToLower() == normalized) ||
            (u.EntraUpn != null && u.EntraUpn.ToLower().StartsWith(prefixMatch)), ct);
    }

    public async Task BackfillEntraObjectIdAsync(Guid userAccountId, string entraObjectId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct);
        if (account == null || !string.IsNullOrEmpty(account.EntraObjectId)) return; // already backfilled or gone

        account.EntraObjectId = entraObjectId;
        await db.SaveChangesAsync(ct);
    }

    public async Task<UserAccount?> GetByLocalUsernameAsync(string username, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<UserAccount>().AsNoTracking()
            .FirstOrDefaultAsync(u => u.LocalUsername == username && u.LocalLoginEnabled && u.IsActive, ct);
    }

    public async Task<List<string>> GetEffectivePermissionsAsync(Guid roleGroupId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<RoleGroupPermission>().AsNoTracking()
            .Where(p => p.RoleGroupId == roleGroupId)
            .Select(p => p.PermissionKey)
            .ToListAsync(ct);
    }

    public async Task<List<RoleGroup>> ListRoleGroupsAsync(CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var groups = await db.Set<RoleGroup>().AsNoTracking().OrderBy(g => g.Name).ToListAsync(ct);
        var allPerms = await db.Set<RoleGroupPermission>().AsNoTracking().ToListAsync(ct);
        foreach (var g in groups)
            g.Permissions = allPerms.Where(p => p.RoleGroupId == g.Id).ToList();
        return groups;
    }

    public async Task<RoleGroup> CreateRoleGroupAsync(string name, string? description, List<string> permissionKeys, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        if (await db.Set<RoleGroup>().AnyAsync(g => g.Name == name, ct))
            throw new InvalidOperationException($"A role group named \"{name}\" already exists.");

        var group = new RoleGroup
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = description,
            IsSystemDefined = false,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByObjectId = _user.ObjectId,
            CreatedByDisplayName = _user.DisplayName
        };
        db.Set<RoleGroup>().Add(group);

        foreach (var key in permissionKeys.Distinct())
            db.Set<RoleGroupPermission>().Add(new RoleGroupPermission { Id = Guid.NewGuid(), RoleGroupId = group.Id, PermissionKey = key });

        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin", "RoleGroup", group.Id.ToString(), "Create",
            newValue: name, context: $"{permissionKeys.Count} permission(s)", ct: ct);
        return group;
    }

    public async Task UpdateRoleGroupPermissionsAsync(Guid roleGroupId, List<string> permissionKeys, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var group = await db.Set<RoleGroup>().FirstOrDefaultAsync(g => g.Id == roleGroupId, ct)
            ?? throw new InvalidOperationException("Role group not found.");

        // Guard against locking every admin out of Access Management
        // entirely. If this is the ONLY role group holding either of these
        // two permissions, and the new permission set would remove both,
        // there would be no way back in through the UI at all afterward -
        // fixing role group permissions itself requires being inside the
        // very module this would make unreachable.
        var criticalPerms = new[] { "Admin.ManageUsers", "Admin.ManageRoleGroups" };
        var stillHasCritical = permissionKeys.Any(p => criticalPerms.Contains(p));
        if (!stillHasCritical)
        {
            var otherGroupsWithCritical = await db.Set<RoleGroupPermission>()
                .Where(p => p.RoleGroupId != roleGroupId && criticalPerms.Contains(p.PermissionKey))
                .Select(p => p.RoleGroupId)
                .Distinct()
                .CountAsync(ct);
            if (otherGroupsWithCritical == 0)
                throw new InvalidOperationException("This would remove the last remaining access to Access Management across the entire system - no role group would be left able to manage users or role groups. Keep at least one of these two permissions here, or grant one of them to another role group first.");
        }

        var existing = await db.Set<RoleGroupPermission>().Where(p => p.RoleGroupId == roleGroupId).ToListAsync(ct);
        db.Set<RoleGroupPermission>().RemoveRange(existing);

        foreach (var key in permissionKeys.Distinct())
            db.Set<RoleGroupPermission>().Add(new RoleGroupPermission { Id = Guid.NewGuid(), RoleGroupId = roleGroupId, PermissionKey = key });

        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin", "RoleGroup", roleGroupId.ToString(), "UpdatePermissions",
            context: $"{group.Name}: {permissionKeys.Count} permission(s)", ct: ct);
    }

    public async Task<UserAccount> CreateUserAccountAsync(UserAccount account, string? localPassword, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        if (!await db.Set<RoleGroup>().AnyAsync(g => g.Id == account.RoleGroupId, ct))
            throw new InvalidOperationException("Role group not found.");

        account.Id = Guid.NewGuid();
        account.CreatedAtUtc = DateTime.UtcNow;
        account.CreatedByObjectId = _user.ObjectId;
        account.CreatedByDisplayName = _user.DisplayName;

        // Enforced here too, not just via disabled fields in the UI - local
        // login accounts are a break-glass mechanism specifically meant to
        // work even when Entra is unavailable, so an Entra identity on one
        // would defeat the point.
        if (account.LocalLoginEnabled)
        {
            account.EntraUpn = null;
            account.EntraObjectId = null;

            if (!string.IsNullOrWhiteSpace(account.LocalUsername))
            {
                var normalized = account.LocalUsername.Trim().ToLower();
                var prefixMatch = normalized + "@";
                var collision = await db.Set<UserAccount>().FirstOrDefaultAsync(u =>
                    (u.LocalUsername != null && u.LocalUsername.ToLower() == normalized) ||
                    (u.EntraUpn != null && u.EntraUpn.ToLower().StartsWith(prefixMatch)), ct);
                if (collision != null)
                    throw new InvalidOperationException($"This username is already in use by \"{collision.DisplayName}\".");
            }
        }

        if (account.LocalLoginEnabled && !string.IsNullOrWhiteSpace(localPassword))
            account.LocalPasswordHash = _hasher.HashPassword(account, localPassword);

        db.Set<UserAccount>().Add(account);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Admin", "UserAccount", account.Id.ToString(), "Create",
            newValue: account.DisplayName, ct: ct);
        return account;
    }

    public async Task SetLocalPasswordAsync(Guid userAccountId, string newPassword, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct)
            ?? throw new InvalidOperationException("User account not found.");

        account.LocalPasswordHash = _hasher.HashPassword(account, newPassword);
        account.LocalLoginEnabled = true;
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Admin", "UserAccount", account.Id.ToString(), "SetLocalPassword",
            context: account.DisplayName, ct: ct);
    }

    public async Task<LocalLoginAttemptResult> VerifyLocalLoginAsync(string username, string password, CancellationToken ct = default)
    {
        // Common, industry-standard thresholds (OWASP's authentication
        // guidance) - enough failed attempts to rule out an honest typo,
        // short enough that a legitimate admin locked out by mistake
        // isn't stuck for long, long enough that it meaningfully slows
        // down anyone actually guessing.
        const int MaxFailedAttempts = 5;
        var lockoutDuration = TimeSpan.FromMinutes(15);

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>()
            .FirstOrDefaultAsync(u => u.LocalUsername == username && u.LocalLoginEnabled && u.IsActive, ct);

        if (account == null || string.IsNullOrEmpty(account.LocalPasswordHash))
            return new LocalLoginAttemptResult(LocalLoginOutcome.InvalidCredentials);

        // Checked before the password is even looked at - once locked,
        // every attempt is rejected outright until the lockout expires,
        // so continuing to guess accomplishes nothing.
        if (account.LockoutEndUtc.HasValue && account.LockoutEndUtc.Value > DateTime.UtcNow)
            return new LocalLoginAttemptResult(LocalLoginOutcome.LockedOut, LockoutRemaining: account.LockoutEndUtc.Value - DateTime.UtcNow);

        var verifyResult = _hasher.VerifyHashedPassword(account, account.LocalPasswordHash, password);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            account.FailedLoginAttempts++;
            if (account.FailedLoginAttempts >= MaxFailedAttempts)
            {
                account.LockoutEndUtc = DateTime.UtcNow.Add(lockoutDuration);
                await db.SaveChangesAsync(ct);
                return new LocalLoginAttemptResult(LocalLoginOutcome.LockedOut, LockoutRemaining: lockoutDuration);
            }
            await db.SaveChangesAsync(ct);
            return new LocalLoginAttemptResult(LocalLoginOutcome.InvalidCredentials);
        }

        // Success - reset the counter entirely, not just clear the
        // lockout flag, so old near-misses don't carry forward.
        account.FailedLoginAttempts = 0;
        account.LockoutEndUtc = null;
        account.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return new LocalLoginAttemptResult(LocalLoginOutcome.Success, account);
    }

    public async Task<UserAccount?> EnsureBootstrapAdminAccountAsync(string entraObjectId, string displayName, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        var existing = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId, ct);
        if (existing != null) return existing;

        // This check IS the security boundary now - not a formality.
        // Guid.TryParse, not just a non-empty comparison, so a leftover
        // placeholder value in config (e.g. still literally
        // "PASTE_YOUR_..._HERE") can never accidentally match anything -
        // same reasoning as the equivalent fix applied to the Program.cs
        // startup bootstrap a while back.
        var configuredBootstrapId = _config["Authorization:BootstrapGlobalAdminObjectId"];
        var isConfiguredBootstrapAdmin =
            !string.IsNullOrWhiteSpace(configuredBootstrapId) &&
            Guid.TryParse(configuredBootstrapId, out _) &&
            string.Equals(configuredBootstrapId, entraObjectId, StringComparison.OrdinalIgnoreCase);

        if (!isConfiguredBootstrapAdmin)
            return null;

        var globalAdminGroup = await db.Set<RoleGroup>().FirstOrDefaultAsync(g => g.Name == "Global Administrator", ct)
            ?? throw new InvalidOperationException("Global Administrator role group not found — run the Administration foundation SQL script first.");

        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            DisplayName = displayName,
            EntraObjectId = entraObjectId,
            RoleGroupId = globalAdminGroup.Id,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByObjectId = "system",
            CreatedByDisplayName = "Bootstrap (Authorization:BootstrapGlobalAdminObjectId)"
        };
        db.Set<UserAccount>().Add(account);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Admin", "UserAccount", account.Id.ToString(), "AutoProvision",
            newValue: displayName, context: "First sign-in of the configured bootstrap Global Administrator", ct: ct);
        return account;
    }

    public async Task DeactivateUserAccountAsync(Guid userAccountId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct)
            ?? throw new InvalidOperationException("Account not found.");
        account.IsActive = false;
        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin", "UserAccount", account.Id.ToString(), "Deactivate",
            context: account.DisplayName, ct: ct);
    }

    public async Task ReactivateUserAccountAsync(Guid userAccountId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct)
            ?? throw new InvalidOperationException("Account not found.");
        account.IsActive = true;
        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin", "UserAccount", account.Id.ToString(), "Reactivate",
            context: account.DisplayName, ct: ct);
    }

    public async Task DeleteUserAccountAsync(Guid userAccountId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct)
            ?? throw new InvalidOperationException("Account not found.");
        var displayName = account.DisplayName;
        db.Set<UserAccount>().Remove(account);
        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin", "UserAccount", userAccountId.ToString(), "Delete",
            context: displayName, ct: ct);
    }

    public async Task UpdateUserAccountDetailsAsync(Guid userAccountId, string displayName, Guid? staffId, string? entraUpn = null, string? entraObjectId = null, bool localLoginEnabled = false, string? localUsername = null, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct)
            ?? throw new InvalidOperationException("Account not found.");
        var oldName = account.DisplayName;

        // Accounts already linked to a Staff profile were provisioned
        // through the onboarding flow - identity fields (including whether
        // local login is even enabled, since a linked account inherently
        // has an Entra identity and the two are mutually exclusive) stay
        // governed by that flow, not hand-edited here. Enforced server-side
        // too, not just via disabled fields in the UI - a previously-linked
        // account's identity is left exactly as it was, regardless of what
        // was passed in for these specific parameters.
        var wasLinked = account.StaffId.HasValue;
        if (!wasLinked)
        {
            // Checked here, not just left to the database's own unique
            // index - that index only compares LocalUsername against other
            // LocalUsername values, so it can't catch a collision against
            // someone else's Entra UPN prefix, which this also checks for.
            if (localLoginEnabled && !string.IsNullOrWhiteSpace(localUsername))
            {
                var normalized = localUsername.Trim().ToLower();
                var prefixMatch = normalized + "@";
                var collision = await db.Set<UserAccount>().FirstOrDefaultAsync(u =>
                    u.Id != userAccountId &&
                    ((u.LocalUsername != null && u.LocalUsername.ToLower() == normalized) ||
                     (u.EntraUpn != null && u.EntraUpn.ToLower().StartsWith(prefixMatch))), ct);
                if (collision != null)
                    throw new InvalidOperationException($"This username is already in use by \"{collision.DisplayName}\".");
            }

            account.DisplayName = displayName;
            account.StaffId = staffId;
            account.LocalLoginEnabled = localLoginEnabled;
            account.LocalUsername = localUsername;
            // Same mutual-exclusivity rule as CreateUserAccountAsync -
            // local login and an Entra identity never coexist.
            account.EntraUpn = localLoginEnabled ? null : entraUpn;
            account.EntraObjectId = localLoginEnabled ? null : entraObjectId;
        }

        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin", "UserAccount", account.Id.ToString(), "UpdateDetails",
            oldValue: oldName, newValue: wasLinked ? oldName : displayName,
            context: wasLinked ? "Linked account - identity fields left unchanged" : null, ct: ct);
    }

    public async Task AssignRoleGroupAsync(Guid userAccountId, Guid roleGroupId, DateTime? expiresAtUtc, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct)
            ?? throw new InvalidOperationException("Account not found.");
        var newGroup = await db.Set<RoleGroup>().FirstOrDefaultAsync(g => g.Id == roleGroupId, ct)
            ?? throw new InvalidOperationException("Role group not found.");

        var oldRoleGroupId = account.RoleGroupId;
        account.RoleGroupId = roleGroupId;
        account.RoleGroupExpiresAtUtc = expiresAtUtc;
        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin", "UserAccount", account.Id.ToString(), "AssignRoleGroup",
            oldValue: oldRoleGroupId.ToString(), newValue: $"{newGroup.Name}{(expiresAtUtc.HasValue ? $" (expires {expiresAtUtc:dd MMM yyyy})" : "")}",
            context: account.DisplayName, ct: ct);
    }

    public async Task SyncDisplayNameAsync(Guid userAccountId, string displayName, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct);
        if (account == null || account.DisplayName == displayName) return;
        account.DisplayName = displayName;
        await db.SaveChangesAsync(ct);
    }

    public async Task UpdateLastLoginAsync(Guid userAccountId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct);
        if (account == null) return;
        account.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<RoleGroup?> GetRoleGroupByIdAsync(Guid roleGroupId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var group = await db.Set<RoleGroup>().AsNoTracking().FirstOrDefaultAsync(g => g.Id == roleGroupId, ct);
        if (group == null) return null;
        group.Permissions = await db.Set<RoleGroupPermission>().AsNoTracking()
            .Where(p => p.RoleGroupId == roleGroupId).ToListAsync(ct);
        return group;
    }

    public async Task EnableLocalLoginOverrideAsync(Guid userAccountId, string localUsername, string password, CancellationToken ct = default)
    {
        // Enforced here, not just via a disabled checkbox in the UI - this
        // is the one path that can override the identity lock protecting
        // a linked account's Entra fields, so it needs its own real
        // permission check, not just a client-side hint.
        if (!_user.HasPermission("Admin.ManageUsers"))
            throw new InvalidOperationException("Only a Global Administrator can convert a linked account to local login.");

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct)
            ?? throw new InvalidOperationException("Account not found.");

        var normalized = localUsername.Trim().ToLower();
        var prefixMatch = normalized + "@";
        var collision = await db.Set<UserAccount>().FirstOrDefaultAsync(u =>
            u.Id != userAccountId &&
            ((u.LocalUsername != null && u.LocalUsername.ToLower() == normalized) ||
             (u.EntraUpn != null && u.EntraUpn.ToLower().StartsWith(prefixMatch))), ct);
        if (collision != null)
            throw new InvalidOperationException($"This username is already in use by \"{collision.DisplayName}\".");

        account.LocalLoginEnabled = true;
        account.LocalUsername = localUsername;
        account.LocalPasswordHash = _hasher.HashPassword(account, password);
        // Archived before clearing, not discarded - RevertToEntraAsync can
        // restore these later, after confirming they're still valid.
        account.ArchivedEntraObjectId = account.EntraObjectId;
        account.ArchivedEntraUpn = account.EntraUpn;
        // Same mutual-exclusivity rule as everywhere else - clearing the
        // Entra identity is the actual "conversion" part of this action.
        account.EntraUpn = null;
        account.EntraObjectId = null;

        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin", "UserAccount", account.Id.ToString(), "EnableLocalLoginOverride",
            context: "Global Admin converted a linked account to local login", ct: ct);
    }

    public async Task DisableLocalLoginOverrideAsync(Guid userAccountId, CancellationToken ct = default)
    {
        if (!_user.HasPermission("Admin.ManageUsers"))
            throw new InvalidOperationException("Only a Global Administrator can change this.");

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct)
            ?? throw new InvalidOperationException("Account not found.");

        account.LocalLoginEnabled = false;
        // Username retained deliberately - re-enabling later shouldn't
        // require retyping it. Password hash still cleared - a dormant
        // account being re-enabled should get a fresh password set
        // explicitly, not silently reactivate whatever was set before.
        account.LocalPasswordHash = null;

        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin", "UserAccount", account.Id.ToString(), "DisableLocalLoginOverride", ct: ct);
    }

    public async Task RevertToEntraAsync(Guid userAccountId, CancellationToken ct = default)
    {
        if (!_user.HasPermission("Admin.ManageUsers"))
            throw new InvalidOperationException("Only a Global Administrator can change this.");

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct)
            ?? throw new InvalidOperationException("Account not found.");

        if (string.IsNullOrWhiteSpace(account.ArchivedEntraObjectId))
            throw new InvalidOperationException("Nothing archived to restore - this account wasn't converted from an Entra-linked account, or was created as local-only from the start.");

        // Verified against Entra directly, not trusted blindly - the
        // conversion never touched the actual Entra account, but someone
        // could have deleted or renamed it independently in the meantime.
        var (found, currentUpn) = await _graphService.VerifyUserAsync(account.ArchivedEntraObjectId, ct);
        if (!found)
            throw new InvalidOperationException($"The archived Entra account (Object ID {account.ArchivedEntraObjectId}) no longer exists - it may have been deleted directly in Entra since this account was converted. Set up a new Entra link manually instead.");

        account.EntraObjectId = account.ArchivedEntraObjectId;
        // Uses whatever UPN Entra reports right now, not the archived one -
        // if it was renamed since the conversion, this reflects the
        // current, correct value rather than restoring a stale one.
        account.EntraUpn = currentUpn ?? account.ArchivedEntraUpn;
        account.ArchivedEntraObjectId = null;
        account.ArchivedEntraUpn = null;
        account.LocalLoginEnabled = false;
        account.LocalUsername = null;
        account.LocalPasswordHash = null;

        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin", "UserAccount", account.Id.ToString(), "RevertToEntra",
            context: "Global Admin reverted a local-login override back to Entra sign-in", ct: ct);
    }

    public async Task UpdateRoleGroupBasicsAsync(Guid roleGroupId, string name, string? description, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var group = await db.Set<RoleGroup>().FirstOrDefaultAsync(g => g.Id == roleGroupId, ct)
            ?? throw new InvalidOperationException("Role group not found.");
        if (group.IsSystemDefined)
            throw new InvalidOperationException("Built-in role groups can't be renamed.");

        group.Name = name;
        group.Description = description;
        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin", "RoleGroup", group.Id.ToString(), "UpdateBasics", newValue: name, ct: ct);
    }

    public async Task DeleteRoleGroupAsync(Guid roleGroupId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var group = await db.Set<RoleGroup>().FirstOrDefaultAsync(g => g.Id == roleGroupId, ct)
            ?? throw new InvalidOperationException("Role group not found.");
        if (group.IsSystemDefined)
            throw new InvalidOperationException("Built-in role groups can't be deleted.");

        var memberCount = await db.Set<UserAccount>().CountAsync(u => u.RoleGroupId == roleGroupId, ct);
        if (memberCount > 0)
            throw new InvalidOperationException($"Can't delete — {memberCount} member(s) are still assigned to this role group. Reassign them first.");

        db.Set<RoleGroup>().Remove(group);
        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin", "RoleGroup", roleGroupId.ToString(), "Delete", context: group.Name, ct: ct);
    }
}
