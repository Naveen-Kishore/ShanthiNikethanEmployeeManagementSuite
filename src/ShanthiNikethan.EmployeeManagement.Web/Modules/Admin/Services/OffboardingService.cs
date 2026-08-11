using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;
using ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Services;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Services;

namespace ShanthiNikethan.EmployeeManagement.Modules.Admin.Services;

public class OffboardResult
{
    public bool Success { get; set; } = true;
    public List<string> Warnings { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// The one place that performs the offboarding cascade - soft-delete the
/// Staff record, deactivate the linked UserAccount, disable the linked
/// Entra account (a real delete, triggering Entra's own native 30-day
/// recovery window). Callable from either side (a Staff ID, from the
/// Staff Profile drawer, or a UserAccount ID, from Access Management) -
/// both produce the identical result, so which drawer you happen to be
/// in when you click Delete never matters.
///
/// Deliberately distinct from DeactivateUserAccountAsync (already
/// existing, unaffected by this) - Deactivate is a temporary access
/// suspension that leaves the Staff record untouched (someone's still
/// employed, just temporarily locked out); this is specifically for an
/// actual departure.
/// </summary>
public interface IOffboardingService
{
    Task<OffboardResult> OffboardByStaffIdAsync(Guid staffId, string? reason, CancellationToken ct = default);

    /// <summary>For a standalone account (no linked Staff profile - e.g. a break-glass admin account), this only affects the UserAccount and Entra side, since there's no Staff record to offboard.</summary>
    Task<OffboardResult> OffboardByUserAccountIdAsync(Guid userAccountId, string? reason, CancellationToken ct = default);
}

public class OffboardingService : IOffboardingService
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly IStaffProfileService _staffService;
    private readonly IUserAccountService _userAccountService;
    private readonly IGraphProvisioningService _graphService;
    private readonly IAuditService _audit;

    public OffboardingService(
        IDbContextFactory<AppDbContext> dbf,
        IStaffProfileService staffService,
        IUserAccountService userAccountService,
        IGraphProvisioningService graphService,
        IAuditService audit)
    {
        _dbf = dbf;
        _staffService = staffService;
        _userAccountService = userAccountService;
        _graphService = graphService;
        _audit = audit;
    }

    public async Task<OffboardResult> OffboardByStaffIdAsync(Guid staffId, string? reason, CancellationToken ct = default)
    {
        var result = new OffboardResult();

        var linkedAccount = await FindAccountByStaffIdAsync(staffId, ct);
        if (linkedAccount != null)
            await CascadeToAccountAsync(linkedAccount, result, ct);

        await _staffService.SoftDeleteAsync(staffId, reason, ct);
        return result;
    }

    public async Task<OffboardResult> OffboardByUserAccountIdAsync(Guid userAccountId, string? reason, CancellationToken ct = default)
    {
        var result = new OffboardResult();

        var account = await _userAccountService.GetByIdAsync(userAccountId, ct);
        if (account == null)
        {
            result.Success = false;
            result.ErrorMessage = "Account not found.";
            return result;
        }

        await CascadeToAccountAsync(account, result, ct);

        if (account.StaffId.HasValue)
            await _staffService.SoftDeleteAsync(account.StaffId.Value, reason, ct);

        return result;
    }

    private async Task<UserAccount?> FindAccountByStaffIdAsync(Guid staffId, CancellationToken ct)
    {
        var all = await _userAccountService.ListUsersAsync(ct);
        return all.FirstOrDefault(u => u.StaffId == staffId);
    }

    private async Task CascadeToAccountAsync(UserAccount account, OffboardResult result, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(account.EntraObjectId))
        {
            var graphResult = await _graphService.DeleteUserAsync(account.EntraObjectId, ct);
            if (!graphResult.Success)
                result.Warnings.Add($"The linked Entra account couldn't be removed automatically: {graphResult.ErrorMessage}. It may need to be handled manually in Entra admin center.");
        }

        try
        {
            await _userAccountService.DeactivateUserAccountAsync(account.Id, ct);
        }
        catch (InvalidOperationException ex)
        {
            // e.g. the self-protection guard against deactivating your own account
            result.Warnings.Add($"The linked account couldn't be deactivated: {ex.Message}");
        }

        await _audit.LogAsync("Admin", "UserAccount", account.Id.ToString(), "Offboard",
            context: "Cascaded from offboarding a linked Staff profile or account", ct: ct);
    }
}
