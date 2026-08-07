using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;

namespace ShanthiNikethan.EmployeeManagement.Modules.Admin.Services;

public interface IUserAccountService
{
    Task<List<UserAccount>> ListUsersAsync(CancellationToken ct = default);
    Task<UserAccount?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<UserAccount?> GetByEntraObjectIdAsync(string objectId, CancellationToken ct = default);
    Task<UserAccount?> GetByEntraUpnAsync(string upn, CancellationToken ct = default);
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

    Task<RoleGroup?> GetRoleGroupByIdAsync(Guid roleGroupId, CancellationToken ct = default);
    Task UpdateRoleGroupBasicsAsync(Guid roleGroupId, string name, string? description, CancellationToken ct = default);

    /// <summary>Deletes a custom role group. Throws if it's built-in (protected) or if any account is still assigned to it - members must be reassigned first, rather than silently orphaned.</summary>
    Task DeleteRoleGroupAsync(Guid roleGroupId, CancellationToken ct = default);

    /// <summary>Verifies local username/password. Returns the matching UserAccount if valid and active, otherwise null. Never throws on bad credentials — a wrong password is not the same as a system error.</summary>
    Task<UserAccount?> VerifyLocalLoginAsync(string username, string password, CancellationToken ct = default);

    /// <summary>
    /// Called on first login by any Entra ID user who passed the
    /// AllowedUserObjectIds allowlist check in Program.cs. Always provisions
    /// as "Regular Staff" — there is no automatic path to Global Administrator
    /// for any Entra account, including the developer's own. To grant Global
    /// Administrator (to yourself or anyone), sign in once via the local
    /// fallback admin account and use "Assign Role Group" in the Admin
    /// Console — the same audited action used for every future promotion,
    /// no special-cased bootstrap logic to reason about. A no-op if the
    /// account already exists.
    /// </summary>
    Task<UserAccount> EnsureBootstrapAdminAccountAsync(string entraObjectId, string displayName, CancellationToken ct = default);

    /// <summary>Updates only the display name - called on every Entra login to keep it current, including replacing a placeholder name set before the account's first real sign-in. Deliberately narrow (one field only) so it's safe to call opportunistically without touching UPN/staff links/local login settings.</summary>
    Task SyncDisplayNameAsync(Guid userAccountId, string displayName, CancellationToken ct = default);
}

public class UserAccountService : IUserAccountService
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;
    private readonly PasswordHasher<UserAccount> _hasher = new();

    public UserAccountService(IDbContextFactory<AppDbContext> dbf, ICurrentUser user, IAuditService audit)
    {
        _dbf = dbf;
        _user = user;
        _audit = audit;
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

    public async Task<UserAccount?> VerifyLocalLoginAsync(string username, string password, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>()
            .FirstOrDefaultAsync(u => u.LocalUsername == username && u.LocalLoginEnabled && u.IsActive, ct);

        if (account == null || string.IsNullOrEmpty(account.LocalPasswordHash))
            return null;

        var result = _hasher.VerifyHashedPassword(account, account.LocalPasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
            return null;

        account.LastLoginAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return account;
    }

    public async Task<UserAccount> EnsureBootstrapAdminAccountAsync(string entraObjectId, string displayName, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        var existing = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.EntraObjectId == entraObjectId, ct);
        if (existing != null)
        {
            // Keep the display name current — this is also what replaces a
            // placeholder name (e.g. "Bootstrap Administrator", set by the
            // startup bootstrap before anyone had actually signed in yet)
            // with the real one, the first time this account logs in.
            if (existing.DisplayName != displayName)
            {
                existing.DisplayName = displayName;
                await db.SaveChangesAsync(ct);
            }
            return existing;
        }

        // Every new Entra account — no exceptions, including the developer's
        // own — starts as Regular Staff. There is no automatic path to
        // Global Administrator here. Promotion (including the very first
        // one) happens manually via the Admin Console's "Assign Role Group"
        // action, signed in as the local fallback admin account — the same
        // audited action used for every future promotion, rather than a
        // special-cased bootstrap rule that behaves differently just once.
        var regularStaffGroup = await db.Set<RoleGroup>().FirstOrDefaultAsync(g => g.Name == "Regular Staff", ct)
            ?? throw new InvalidOperationException("'Regular Staff' role group not found — run the Administration foundation SQL script first.");

        // Defensive: a missing/empty display name (e.g. a token whose claims
        // weren't fully available yet - this is how one earlier test session
        // ended up creating an untraceable blank-named account) should never
        // silently create a nameless, unidentifiable row. Fall back to
        // something that at least shows WHICH account this is; the
        // DisplayName-sync above will overwrite this with the real name
        // automatically the next time this same person signs in normally.
        var safeDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? $"(Unnamed Entra user — {entraObjectId})"
            : displayName;

        var account = new UserAccount
        {
            Id = Guid.NewGuid(),
            DisplayName = safeDisplayName,
            EntraObjectId = entraObjectId,
            RoleGroupId = regularStaffGroup.Id,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByObjectId = "system",
            CreatedByDisplayName = "Auto-provisioned (Entra allowlist)"
        };
        db.Set<UserAccount>().Add(account);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("Admin", "UserAccount", account.Id.ToString(), "AutoProvision",
            newValue: displayName,
            context: "New Entra ID user from allowlist — provisioned as Regular Staff. Promote manually via Admin Console if elevated access is needed.",
            ct: ct);
        return account;
    }

    public async Task SyncDisplayNameAsync(Guid userAccountId, string displayName, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct);
        if (account == null || account.DisplayName == displayName) return;
        account.DisplayName = displayName;
        await db.SaveChangesAsync(ct);
    }

    public async Task DeactivateUserAccountAsync(Guid userAccountId, CancellationToken ct = default)
    {
        if (_user.UserAccountId == userAccountId)
            throw new InvalidOperationException("You cannot deactivate your own account. Sign in as a different Global Administrator to do this.");

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct)
            ?? throw new InvalidOperationException("Account not found.");

        if (account.LocalLoginEnabled)
            throw new InvalidOperationException($"\"{account.DisplayName}\" has local login enabled as an emergency fallback account and can't be deactivated. Disable local login first if you're sure you want to do this.");

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
        if (_user.UserAccountId == userAccountId)
            throw new InvalidOperationException("You cannot delete your own account. Sign in as a different Global Administrator to do this.");

        await using var db = await _dbf.CreateDbContextAsync(ct);
        var account = await db.Set<UserAccount>().FirstOrDefaultAsync(u => u.Id == userAccountId, ct)
            ?? throw new InvalidOperationException("Account not found.");

        if (account.LocalLoginEnabled)
            throw new InvalidOperationException($"\"{account.DisplayName}\" has local login enabled as an emergency fallback account and can't be deleted. Disable local login first if you're sure you want to do this.");

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
        account.DisplayName = displayName;
        account.StaffId = staffId;
        account.EntraUpn = entraUpn;
        account.EntraObjectId = entraObjectId;
        account.LocalLoginEnabled = localLoginEnabled;
        account.LocalUsername = localUsername;
        await db.SaveChangesAsync(ct);
        await _audit.LogAsync("Admin", "UserAccount", account.Id.ToString(), "UpdateDetails",
            oldValue: oldName, newValue: displayName, ct: ct);
    }

    public async Task AssignRoleGroupAsync(Guid userAccountId, Guid roleGroupId, DateTime? expiresAtUtc, CancellationToken ct = default)
    {
        if (_user.UserAccountId == userAccountId)
            throw new InvalidOperationException("You cannot change your own role group. Sign in as a different Global Administrator to do this.");

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

    public async Task<RoleGroup?> GetRoleGroupByIdAsync(Guid roleGroupId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var group = await db.Set<RoleGroup>().AsNoTracking().FirstOrDefaultAsync(g => g.Id == roleGroupId, ct);
        if (group == null) return null;
        group.Permissions = await db.Set<RoleGroupPermission>().AsNoTracking()
            .Where(p => p.RoleGroupId == roleGroupId).ToListAsync(ct);
        return group;
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
