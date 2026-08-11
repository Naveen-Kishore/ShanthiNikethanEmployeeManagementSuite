using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Modules;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;
using ShanthiNikethan.EmployeeManagement.Modules.Admin.Services;

namespace ShanthiNikethan.EmployeeManagement.Modules.Admin;

public class AdminModule : IModule
{
    public string Name => "Admin";
    public string DisplayName => "Access Management";
    public string Icon => "shield-settings";
    public string BasePath => "/admin";
    public int NavigationOrder => 90;
    public string? GroupName => "Administration";
    public string? GroupIcon => "shield-settings"; // last in the nav — this is configuration, not daily-use data

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<IUserAccountService, UserAccountService>();
        services.AddScoped<IOffboardingService, OffboardingService>();
        services.AddScoped<IOnboardingService, OnboardingService>();
    }

    public void ConfigureDbContext(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RoleGroup>(e =>
        {
            e.ToTable("RoleGroup");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(300);
            e.HasIndex(x => x.Name).IsUnique();
            e.Ignore(x => x.Permissions); // navigation populated manually by the service, not EF-mapped
        });

        modelBuilder.Entity<RoleGroupPermission>(e =>
        {
            e.ToTable("RoleGroupPermission");
            e.HasKey(x => x.Id);
            e.Property(x => x.PermissionKey).HasMaxLength(100);
            e.HasIndex(x => new { x.RoleGroupId, x.PermissionKey }).IsUnique();

            e.HasOne<RoleGroup>()
                .WithMany()
                .HasForeignKey(x => x.RoleGroupId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<UserAccount>(e =>
        {
            e.ToTable("UserAccount");
            e.HasKey(x => x.Id);
            e.Property(x => x.DisplayName).HasMaxLength(200);
            e.Property(x => x.EntraObjectId).HasMaxLength(100);
            e.Property(x => x.EntraUpn).HasMaxLength(200);
            e.Property(x => x.ArchivedEntraObjectId).HasMaxLength(100);
            e.Property(x => x.ArchivedEntraUpn).HasMaxLength(200);
            e.Property(x => x.LocalUsername).HasMaxLength(100);
            e.Property(x => x.LocalPasswordHash).HasMaxLength(500);
            e.HasIndex(x => x.EntraObjectId);
            e.HasIndex(x => x.LocalUsername).IsUnique().HasFilter("[LocalUsername] IS NOT NULL");

            e.HasOne<RoleGroup>()
                .WithMany()
                .HasForeignKey(x => x.RoleGroupId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data.Staff>()
                .WithMany()
                .HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.SetNull);
        });
    }
}
