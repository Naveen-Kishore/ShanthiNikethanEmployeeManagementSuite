using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Modules;
using ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Data;
using ShanthiNikethan.EmployeeManagement.Modules.AutomationRules.Services;

namespace ShanthiNikethan.EmployeeManagement.Modules.AutomationRules;

public class AutomationRulesModule : IModule
{
    public string Name => "AutomationRules";
    public string DisplayName => "Automation Rules";
    public string Icon => "settings-adjust";
    public string BasePath => "/automation-rules";
    public int NavigationOrder => 93; // right after Audit Log (92), within the Administration group
    public string? GroupName => "Administration";

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<IGroupAutomationService, GroupAutomationService>();
    }

    public void ConfigureDbContext(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GroupAutomationRule>(e =>
        {
            e.ToTable("GroupAutomationRule");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.RuleName).IsUnique();
        });

        modelBuilder.Entity<StaffAutomationRuleAssignment>(e =>
        {
            e.ToTable("StaffAutomationRuleAssignment");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.StaffId, x.RemovedAtUtc });

            e.HasOne<ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data.Staff>()
                .WithMany()
                .HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<GroupAutomationRule>()
                .WithMany()
                .HasForeignKey(x => x.GroupAutomationRuleId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
