using Microsoft.EntityFrameworkCore;
using ShanthiNikethan.EmployeeManagement.Core.Modules;

namespace ShanthiNikethan.EmployeeManagement.Modules.IdentityProvider;

public class IdentityProviderModule : IModule
{
    public string Name => "IdentityProvider";
    public string DisplayName => "Identity Provider Settings";
    public string Icon => "shield-settings";
    public string BasePath => "/identity-provider";
    public int NavigationOrder => 91; // right after Access Management within the Administration group
    public string? GroupName => "Administration";

    public void RegisterServices(IServiceCollection services) { }
    public void ConfigureDbContext(ModelBuilder modelBuilder) { }
}
