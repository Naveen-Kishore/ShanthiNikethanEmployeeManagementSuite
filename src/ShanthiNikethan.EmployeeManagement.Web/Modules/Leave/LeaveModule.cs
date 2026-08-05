using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Modules;
using ShanthiNikethan.EmployeeManagement.Modules.Leave.Data;
using ShanthiNikethan.EmployeeManagement.Modules.Leave.Services;

namespace ShanthiNikethan.EmployeeManagement.Modules.Leave;

public class LeaveModule : IModule
{
    public string Name => "Leave";
    public string DisplayName => "Leave Management";
    public string Icon => "calendar";
    public string BasePath => "/leave";
    public int NavigationOrder => 15;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<ILeaveService, LeaveService>();
    }

    public void ConfigureDbContext(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<LeaveRecord>(e =>
        {
            e.ToTable("LeaveRecord");
            e.HasKey(x => x.Id);
            e.Property(x => x.Designation).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.DaysCount).HasColumnType("decimal(4,1)");
            e.Property(x => x.Reason).HasMaxLength(200);
            e.Property(x => x.SubstituteArrangementNotes).HasMaxLength(1000);
            e.HasIndex(x => x.StaffId);
            e.HasIndex(x => x.StartDate);

            // Explicit FK config — learned this the hard way with Payroll:
            // without it, EF Core doesn't know the insert/dependency order
            // and the database-level FK constraint alone isn't enough.
            e.HasOne<ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data.Staff>()
                .WithMany()
                .HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
