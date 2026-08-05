using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Data;
using ShanthiNikethan.EmployeeManagement.Core.Services;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;

namespace ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Services;

public interface ISubDesignationService
{
    Task<List<SubDesignationOption>> ListAsync(StaffDesignation category, CancellationToken ct = default);
    Task<SubDesignationOption> AddAsync(StaffDesignation category, string name, CancellationToken ct = default);
}

public class SubDesignationService : ISubDesignationService
{
    private readonly IDbContextFactory<AppDbContext> _dbf;
    private readonly IAuditService _audit;

    public SubDesignationService(IDbContextFactory<AppDbContext> dbf, IAuditService audit)
    {
        _dbf = dbf;
        _audit = audit;
    }

    public async Task<List<SubDesignationOption>> ListAsync(StaffDesignation category, CancellationToken ct = default)
    {
        await using var db = await _dbf.CreateDbContextAsync(ct);
        return await db.Set<SubDesignationOption>()
            .Where(o => o.Category == category && o.IsActive)
            .OrderBy(o => o.DisplayOrder).ThenBy(o => o.Name)
            .ToListAsync(ct);
    }

    public async Task<SubDesignationOption> AddAsync(StaffDesignation category, string name, CancellationToken ct = default)
    {
        name = name.Trim();
        await using var db = await _dbf.CreateDbContextAsync(ct);

        var existing = await db.Set<SubDesignationOption>()
            .FirstOrDefaultAsync(o => o.Category == category && o.Name.ToLower() == name.ToLower(), ct);
        if (existing != null)
        {
            if (!existing.IsActive) { existing.IsActive = true; await db.SaveChangesAsync(ct); }
            return existing;
        }

        var maxOrder = await db.Set<SubDesignationOption>()
            .Where(o => o.Category == category)
            .MaxAsync(o => (int?)o.DisplayOrder, ct) ?? 0;

        var option = new SubDesignationOption
        {
            Category = category,
            Name = name,
            DisplayOrder = maxOrder + 1,
            IsActive = true
        };
        db.Set<SubDesignationOption>().Add(option);
        await db.SaveChangesAsync(ct);

        await _audit.LogAsync("StaffProfile", "SubDesignationOption", option.Id.ToString(), "Create",
            newValue: name, context: $"New sub-designation under {category}", ct: ct);

        return option;
    }
}
