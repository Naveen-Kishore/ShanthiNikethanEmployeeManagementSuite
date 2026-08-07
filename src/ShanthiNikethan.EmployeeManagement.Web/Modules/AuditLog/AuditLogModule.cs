using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Modules;

namespace ShanthiNikethan.EmployeeManagement.Modules.AuditLog;

public class AuditLogModule : IModule
{
    public string Name => "AuditLog";
    public string DisplayName => "Audit Log";
    public string Icon => "file-text";
    public string BasePath => "/audit-log";
    public int NavigationOrder => 92; // right after Identity Provider Settings, within the Administration group
    public string? GroupName => "Administration";

    // No new services or entities - this module is entirely a UI on top of
    // IAuditService/AuditLogEntry, which already exist and are already
    // populated by role changes, account creation, deletion, etc. across
    // the rest of the app. Sign-in events are the one addition made
    // alongside this module - see LocalAccountController.cs and
    // MainLayout.razor.
    public void RegisterServices(IServiceCollection services) { }
    public void ConfigureDbContext(ModelBuilder modelBuilder) { }
}
