using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Modules;
using ShanthiNikethan.EmployeeManagement.Modules.Payroll.Data;
using ShanthiNikethan.EmployeeManagement.Modules.Payroll.Services;

namespace ShanthiNikethan.EmployeeManagement.Modules.Payroll;

public class PayrollModule : IModule
{
    public string Name => "Payroll";
    public string DisplayName => "Payroll";
    public string Icon => "file-text";
    public string BasePath => "/payroll";
    public int NavigationOrder => 20;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<IPayrollService, PayrollService>();
        services.AddScoped<IPayrollExportService, PayrollExportService>();
    }

    public void ConfigureDbContext(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PayrollRun>(e =>
        {
            e.ToTable("PayrollRun");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.RunType).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.OtherLabel).HasMaxLength(100);
            e.Ignore(x => x.MonthLabel);
            e.Ignore(x => x.RunTypeLabel);
            e.Ignore(x => x.IsRegularSalary);
            e.HasIndex(x => new { x.Year, x.Month, x.RunType }).IsUnique();
        });

        modelBuilder.Entity<PayrollLineItem>(e =>
        {
            e.ToTable("PayrollLineItem");
            e.HasKey(x => x.Id);
            e.Property(x => x.Designation).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.BankMode).HasConversion<byte>();
            e.Property(x => x.NetPay).HasColumnType("decimal(12,2)");
            e.HasIndex(x => x.PayrollRunId);

            // Tell EF Core about the FK relationship explicitly. The database
            // constraint alone isn't enough — without this, EF Core doesn't
            // know PayrollRun must be inserted before its PayrollLineItems in
            // the same SaveChanges call, and gets the insert order wrong.
            e.HasOne<PayrollRun>()
                .WithMany()
                .HasForeignKey(x => x.PayrollRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
