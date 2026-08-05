using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Modules;
using ShanthiNikethan.EmployeeManagement.Modules.Dashboard.Services;

namespace ShanthiNikethan.EmployeeManagement.Modules.Dashboard;

public class DashboardModule : IModule
{
    public string Name => "Dashboard";
    public string DisplayName => "Dashboard";
    public string Icon => "chart-bar";
    public string BasePath => "/dashboard";
    public int NavigationOrder => 5;

    public void RegisterServices(IServiceCollection services)
    {
        services.AddScoped<IDashboardService, DashboardService>();
    }

    public void ConfigureDbContext(ModelBuilder modelBuilder)
    {
        // No entities of its own — pure aggregation over Staff, Payroll,
        // Leave, and the audit log, all already configured by their own modules.
    }
}
