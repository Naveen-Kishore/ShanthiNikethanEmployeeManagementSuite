using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;
using ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Services;

namespace ShanthiNikethan.EmployeeManagement.Modules.Admin.Services;

public class OnboardResult
{
    public bool Success { get; set; } = true;
    public string? ErrorMessage { get; set; }
    public string? TemporaryPassword { get; set; }
    public List<string> Warnings { get; set; } = new();
}

/// <summary>
/// Creates an Entra account, applies selected automation rules, and links
/// a UserAccount - the exact logic Add Staff's "Enable sign-in" flow
/// already uses, extracted here so it's the single source of truth for
/// both that original creation-time flow AND the retroactive "enable
/// sign-in later" flow on an already-existing staff member's drawer.
/// Extracting this was specifically to avoid two separate, slowly-
/// drifting copies of the same non-trivial orchestration.
/// </summary>
public interface IOnboardingService
{
    /// <summary>onProgress, if given, is called with a short status string at each major step - lets the caller show live progress text, matching what the original inline version already did before this logic was extracted.</summary>
    Task<OnboardResult> EnableSignInAsync(Guid staffId, string staffDisplayName, string upn,
                                           List<Guid> automationRuleIds, Action<string>? onProgress = null,
                                           CancellationToken ct = default);
}

public class OnboardingService : IOnboardingService
{
    private readonly IUserAccountService _userAccountService;
    private readonly IGraphProvisioningService _graphService;
    private readonly IGroupAutomationService _automationService;
    private readonly IDashboardNotificationService _notificationService;

    public OnboardingService(
        IUserAccountService userAccountService,
        IGraphProvisioningService graphService,
        IGroupAutomationService automationService,
        IDashboardNotificationService notificationService)
    {
        _userAccountService = userAccountService;
        _graphService = graphService;
        _automationService = automationService;
        _notificationService = notificationService;
    }

    public async Task<OnboardResult> EnableSignInAsync(Guid staffId, string staffDisplayName, string upn,
                                                        List<Guid> automationRuleIds, Action<string>? onProgress = null,
                                                        CancellationToken ct = default)
    {
        var result = new OnboardResult();

        try
        {
            // Checked first, not just left to Graph's own rejection - same
            // reasoning as the original creation-time check this was
            // extracted from.
            var existingAccount = await _userAccountService.FindAnyByEntraUpnAsync(upn, ct);
            if (existingAccount != null)
            {
                result.Success = false;
                result.ErrorMessage = $"This UPN is already on file here, linked to \"{existingAccount.DisplayName}\"" +
                                       (existingAccount.IsActive ? "." : " (currently deactivated).") +
                                       " Use a different UPN, or manage that existing account from Access Management instead.";
                return result;
            }

            onProgress?.Invoke("Creating Entra sign-in…");
            var graphResult = await _graphService.CreateUserAsync(staffDisplayName, upn, ct);
            if (!graphResult.Success)
            {
                result.Success = false;
                result.ErrorMessage = graphResult.ErrorMessage;
                return result;
            }

            result.TemporaryPassword = graphResult.TemporaryPassword;
            var newObjectId = graphResult.ObjectId!;

            var availableRules = await _automationService.ListRulesAsync(enabledOnly: true, ct: ct);
            foreach (var ruleId in automationRuleIds)
            {
                var rule = availableRules.FirstOrDefault(r => r.Id == ruleId);
                if (rule == null) continue;

                onProgress?.Invoke($"Applying \"{rule.RuleName}\"…");
                var groupResult = await _graphService.AddToGroupAsync(newObjectId, rule.EntraGroupObjectId, ct);
                if (groupResult.Success)
                    await _automationService.RecordAssignmentAsync(staffId, rule.Id, ct);
                else
                    result.Warnings.Add($"\"{rule.RuleName}\" couldn't be applied: {groupResult.ErrorMessage}");
            }

            onProgress?.Invoke("Linking the app account…");
            var roleGroups = await _userAccountService.ListRoleGroupsAsync(ct);
            var regularStaffGroup = roleGroups.FirstOrDefault(g => g.Name == "Regular Staff");
            if (regularStaffGroup == null)
            {
                result.Warnings.Add("Couldn't find the \"Regular Staff\" role group — the Entra account was created, but no app login was linked. Set this up manually from Access Management.");
                return result;
            }

            await _userAccountService.CreateUserAccountAsync(new UserAccount
            {
                DisplayName = staffDisplayName,
                EntraUpn = upn,
                EntraObjectId = newObjectId,
                StaffId = staffId,
                RoleGroupId = regularStaffGroup.Id,
                LocalLoginEnabled = false
            }, null, ct);

            await _notificationService.CreateAsync(
                $"{staffDisplayName} was just added and needs their salary set.",
                "Correspondent", $"/staff?open={staffId}&tab=Salary", ct: ct);

            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.ErrorMessage = $"Sign-in setup hit an unexpected problem: {ex.Message}";
            return result;
        }
    }
}
