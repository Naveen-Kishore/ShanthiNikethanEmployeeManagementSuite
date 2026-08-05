using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Modules;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;
using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Services;

namespace ShanthiNikethan.EmployeeManagement.Modules.StaffProfile;

public class StaffProfileModule : IModule
{
    public string Name => "StaffProfile";
    public string DisplayName => "Staff Directory";
    public string Icon => "users";
    public string BasePath => "/staff";
    public int NavigationOrder => 10;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<StatutorySalaryCalculator>();
        services.AddScoped<IStaffProfileService, StaffProfileService>();
        services.AddScoped<IPhotoStorageService, PhotoStorageService>();
        services.AddScoped<ISubDesignationService, SubDesignationService>();
    }

    public void ConfigureDbContext(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Staff>(e =>
        {
            e.ToTable("Staff");
            e.HasKey(x => x.Id);

            e.Property(x => x.Id).HasDefaultValueSql("NEWSEQUENTIALID()");
            e.Property(x => x.StaffCode).IsRequired().HasMaxLength(20);
            e.Property(x => x.Designation).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.SubDesignation).HasMaxLength(50);
            e.Ignore(x => x.IsEpfEnabled);
            e.Ignore(x => x.IsEsicEnabled);
            e.Property(x => x.BankMode).HasConversion<byte>();
            e.Property(x => x.GrossPay).HasColumnType("decimal(12,2)");
            e.Property(x => x.NetPayOverride).HasColumnType("decimal(12,2)");
            e.Property(x => x.CreatedAtUtc).HasDefaultValueSql("SYSUTCDATETIME()");

            e.HasIndex(x => x.StaffCode).IsUnique();
            e.HasIndex(x => x.BankAccountNumber).IsUnique();
            e.HasIndex(x => new { x.IsActive, x.DisplayOrder });
            e.HasIndex(x => x.SoftDeletedAtUtc);
        });

        modelBuilder.Entity<SubDesignationOption>(e =>
        {
            e.ToTable("SubDesignationOption");
            e.HasKey(x => x.Id);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Name).IsRequired().HasMaxLength(50);
            e.HasIndex(x => new { x.Category, x.Name }).IsUnique();
        });
    }
}
