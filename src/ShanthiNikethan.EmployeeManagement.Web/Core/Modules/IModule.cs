using Microsoft.EntityFrameworkCore;

namespace ShanthiNikethan.EmployeeManagement.Core.Modules;

/// <summary>
/// Every feature module implements this interface. The <see cref="ModuleRegistry"/>
/// discovers implementations, filters them by the configuration in modules.json,
/// and calls <see cref="RegisterServices"/> + <see cref="ConfigureDbContext"/>
/// on the enabled subset at application startup.
/// </summary>
public interface IModule
{
    /// <summary>Unique identifier; must match the key in modules.json.</summary>
    string Name { get; }

    /// <summary>Human-readable label shown in the navigation sidebar.</summary>
    string DisplayName { get; }

    /// <summary>Lucide icon name (e.g. "users", "briefcase", "calendar").</summary>
    string Icon { get; }

    /// <summary>Root path for the module's routes, e.g. "/staff".</summary>
    string BasePath { get; }

    /// <summary>Sort order in the sidebar. Lower numbers appear higher.</summary>
    int NavigationOrder { get; }

    /// <summary>Register DI services required by this module.</summary>
    void RegisterServices(IServiceCollection services);

    /// <summary>Add this module's entities to the shared DbContext.</summary>
    void ConfigureDbContext(ModelBuilder modelBuilder);
}
