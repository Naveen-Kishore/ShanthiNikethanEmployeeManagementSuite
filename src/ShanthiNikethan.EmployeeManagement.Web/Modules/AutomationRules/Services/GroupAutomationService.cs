using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Data;

namespace ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Services;

public interface IGroupAutomationService
{
    /// <summary>enabledOnly: true for the Office Admin's Add Staff checklist (only rules that currently apply), false for Global Admin's management screen (needs to see disabled rules too, to re-enable them).</summary>
    Task<List<GroupAutomationRule>> ListRulesAsync(bool enabledOnly, CancellationToken ct = default);

    Task<GroupAutomationRule> CreateRuleAsync(string ruleName, string? description, string entraGroupObjectId, CancellationToken ct = default);
    Task UpdateRuleAsync(Guid id, string ruleName, string? description, string entraGroupObjectId, bool isEnabled, CancellationToken ct = default);

    /// <summary>Blocked (throws) if this rule has any assignment history - matches the existing "can't delete a role group with members" pattern. Disable it instead if it's no longer wanted going forward.</summary>
    Task DeleteRuleAsync(Guid id, CancellationToken ct = default);

    // ---- Assignment tracking - not called by anything yet (that's Stage 3/4's
    // onboarding/offboarding flow), added now since the table already exists
    // and this is exactly the kind of basic plumbing this stage is meant to
    // finish so later stages don't need to revisit this file for it. ----

    Task RecordAssignmentAsync(Guid staffId, Guid groupAutomationRuleId, CancellationToken ct = default);

    /// <summary>Marks the assignment as removed (RemovedAtUtc set) rather than deleting the row - offboarding needs this history intact so reactivation knows what to reapply.</summary>
    Task RemoveAssignmentAsync(Guid staffId, Guid groupAutomationRuleId, CancellationToken ct = default);

    /// <summary>Currently-active (RemovedAtUtc is null) assignments for a staff member - what reactivation reads to know which rules to reapply.</summary>
    Task<List<StaffAutomationRuleAssignment>> GetActiveAssignmentsForStaffAsync(Guid staffId, CancellationToken ct = default);
}

public class GroupAutomationService : IGroupAutomationService
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly ICurrentUser _user;
    private readonly IAuditService _audit;

    public GroupAutomationService(IDbContextFactory<AppDbContext> dbf, ICurrentUser user, IAuditService audit)
    {
        _dbf = dbf;
        _user = user;
        _audit = audit;
    }

    public async Task<List<GroupAutomationRule>> ListRulesAsync(bool enabledOnly, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var query = db.Set<GroupAutomationRule>().AsNoTracking().AsQueryable();
        if (enabledOnly) query = query.Where(r => r.IsEnabled);
        return await query.OrderBy(r => r.DisplayOrder).ThenBy(r => r.RuleName).ToListAsync(ct);
    }

    public async Task<GroupAutomationRule> CreateRuleAsync(string ruleName, string? description, string entraGroupObjectId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);

        if (await db.Set<GroupAutomationRule>().AnyAsync(r => r.RuleName == ruleName, ct))
            throw new InvalidOperationException($"A rule named \"{ruleName}\" already exists.");

        var maxOrder = await db.Set<GroupAutomationRule>().Select(r => (int?)r.DisplayOrder).MaxAsync(ct) ?? 0;

        var rule = new GroupAutomationRule
        {
            Id = Guid.NewGuid(),
            RuleName = ruleName,
            Description = description,
            EntraGroupObjectId = entraGroupObjectId,
            IsEnabled = true,
            DisplayOrder = maxOrder + 1,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByObjectId = _user.ObjectId,
            CreatedByDisplayName = _user.DisplayName
        };
        db.Set<GroupAutomationRule>().Add(rule);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("AutomationRules", "GroupAutomationRule", rule.Id.ToString(), "Create",
            newValue: ruleName, context: $"Entra group {entraGroupObjectId}", ct: ct);
        return rule;
    }

    public async Task UpdateRuleAsync(Guid id, string ruleName, string? description, string entraGroupObjectId, bool isEnabled, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var rule = await db.Set<GroupAutomationRule>().FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new InvalidOperationException("Rule not found.");

        if (ruleName != rule.RuleName && await db.Set<GroupAutomationRule>().AnyAsync(r => r.RuleName == ruleName && r.Id != id, ct))
            throw new InvalidOperationException($"A rule named \"{ruleName}\" already exists.");

        var oldEnabled = rule.IsEnabled;
        rule.RuleName = ruleName;
        rule.Description = description;
        rule.EntraGroupObjectId = entraGroupObjectId;
        rule.IsEnabled = isEnabled;
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("AutomationRules", "GroupAutomationRule", rule.Id.ToString(), "Update",
            newValue: ruleName,
            context: oldEnabled != isEnabled ? (isEnabled ? "Enabled" : "Disabled") : null, ct: ct);
    }

    public async Task DeleteRuleAsync(Guid id, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var rule = await db.Set<GroupAutomationRule>().FirstOrDefaultAsync(r => r.Id == id, ct)
            ?? throw new InvalidOperationException("Rule not found.");

        var everUsed = await db.Set<StaffAutomationRuleAssignment>().AnyAsync(a => a.GroupAutomationRuleId == id, ct);
        if (everUsed)
            throw new InvalidOperationException($"\"{rule.RuleName}\" has assignment history and can't be deleted — disable it instead if it's no longer needed going forward.");

        db.Set<GroupAutomationRule>().Remove(rule);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("AutomationRules", "GroupAutomationRule", id.ToString(), "Delete", newValue: rule.RuleName, ct: ct);
    }

    public async Task RecordAssignmentAsync(Guid staffId, Guid groupAutomationRuleId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        db.Set<StaffAutomationRuleAssignment>().Add(new StaffAutomationRuleAssignment
        {
            Id = Guid.NewGuid(),
            StaffId = staffId,
            GroupAutomationRuleId = groupAutomationRuleId,
            AppliedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }

    public async Task RemoveAssignmentAsync(Guid staffId, Guid groupAutomationRuleId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        var assignment = await db.Set<StaffAutomationRuleAssignment>()
            .FirstOrDefaultAsync(a => a.StaffId == staffId && a.GroupAutomationRuleId == groupAutomationRuleId && a.RemovedAtUtc == null, ct);
        if (assignment == null) return;
        assignment.RemovedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public async Task<List<StaffAutomationRuleAssignment>> GetActiveAssignmentsForStaffAsync(Guid staffId, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<StaffAutomationRuleAssignment>()
            .Where(a => a.StaffId == staffId && a.RemovedAtUtc == null)
            .ToListAsync(ct);
    }
}
