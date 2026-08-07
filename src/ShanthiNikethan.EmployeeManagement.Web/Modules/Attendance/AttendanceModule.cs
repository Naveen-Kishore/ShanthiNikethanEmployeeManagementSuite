using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Modules;
using ShanthiNikethan.EmployeeManagement.Modules.Attendance.Data;
using ShanthiNikethan.EmployeeManagement.Modules.Attendance.Services;

namespace ShanthiNikethan.EmployeeManagement.Modules.Attendance;

public class AttendanceModule : IModule
{
    public string Name => "Attendance";
    public string DisplayName => "Attendance";
    public string Icon => "clipboard-task-list-rtl";
    public string BasePath => "/attendance";
    public int NavigationOrder => 12; // right after Staff Directory (10), before Leave (15)

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<IAttendanceService, AttendanceService>();
    }

    public void ConfigureDbContext(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AttendanceRecord>(e =>
        {
            e.ToTable("AttendanceRecord");
            e.HasKey(x => x.Id);
            e.Property(x => x.Designation).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.MorningStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.EveningStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Notes).HasMaxLength(300);
            e.Ignore(x => x.PresentDayScore);
            e.HasIndex(x => new { x.StaffId, x.AttendanceDate }).IsUnique();
            e.HasIndex(x => x.AttendanceDate);

            e.HasOne<ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data.Staff>()
                .WithMany()
                .HasForeignKey(x => x.StaffId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
