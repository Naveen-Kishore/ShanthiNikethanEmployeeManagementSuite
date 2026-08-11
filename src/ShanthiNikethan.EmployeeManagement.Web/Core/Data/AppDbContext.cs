using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Modules;
using ShanthiNikethan.EmployeeManagement.Core.Services;

namespace ShanthiNikethan.EmployeeManagement.Core.Data;

/// <summary>
/// The single DbContext for the whole application. Each enabled module
/// contributes entity configurations via its <see cref="IModule.ConfigureDbContext"/>
/// method. Modules that are disabled contribute nothing, so their tables
/// are still created (via SQL scripts) but no code touches them.
/// </summary>
public class AppDbContext : DbContext
{
    private readonly ModuleRegistry _moduleRegistry;

    public AppDbContext(DbContextOptions<AppDbContext> options, ModuleRegistry moduleRegistry)
        : base(options)
    {
        _moduleRegistry = moduleRegistry;
    }

    public DbSet<AuditLogEntry> AuditLog => Set<AuditLogEntry>();
    public DbSet<ModuleStateRecord> ModuleState => Set<ModuleStateRecord>();
    public DbSet<DashboardNotification> DashboardNotifications => Set<DashboardNotification>();
    public DbSet<DashboardNotificationDismissal> DashboardNotificationDismissals => Set<DashboardNotificationDismissal>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Core entities
        modelBuilder.Entity<AuditLogEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.OccurredAtUtc).IsDescending();
            e.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        modelBuilder.Entity<ModuleStateRecord>(e =>
        {
            e.ToTable("ModuleState");
            e.HasKey(x => x.ModuleName);
            e.Property(x => x.LicenseTier).HasConversion<string>();
        });

        modelBuilder.Entity<DashboardNotification>(e =>
        {
            e.ToTable("DashboardNotification");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.TargetRoleGroupName, x.CreatedAtUtc }).IsDescending(false, true);
        });

        modelBuilder.Entity<DashboardNotificationDismissal>(e =>
        {
            e.ToTable("DashboardNotificationDismissal");
            e.HasKey(x => new { x.NotificationId, x.UserAccountId });
        });

        // Delegate to each enabled module to add its own entities
        foreach (var module in _moduleRegistry.EnabledModules)
        {
            module.ConfigureDbContext(modelBuilder);
        }
    }
}
