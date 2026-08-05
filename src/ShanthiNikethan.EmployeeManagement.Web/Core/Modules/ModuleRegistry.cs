using System.Reflection;
using System.Text.Json;

namespace ShanthiNikethan.EmployeeManagement.Core.Modules;

/// <summary>
/// Discovers all IModule implementations in the assembly, filters them by
/// modules.json, and exposes the enabled set to DI and the navigation UI.
/// </summary>
public class ModuleRegistry
{
    private readonly List<IModule> _enabled;
    private readonly List<string> _disabled;
    public ModulesRoot Configuration { get; }

    public IReadOnlyList<IModule> EnabledModules => _enabled;
    public IReadOnlyList<string> DisabledModules => _disabled;

    public ModuleRegistry(ModulesRoot configuration, IEnumerable<IModule> discovered)
    {
        Configuration = configuration;
        _enabled = new List<IModule>();
        _disabled = new List<string>();

        foreach (var module in discovered.OrderBy(m => m.NavigationOrder))
        {
            if (!configuration.Modules.TryGetValue(module.Name, out var cfg))
            {
                _disabled.Add($"{module.Name} (no config)");
                continue;
            }

            if (!cfg.Enabled)
            {
                _disabled.Add($"{module.Name} (disabled)");
                continue;
            }

            if ((int)cfg.LicenseTier > (int)configuration.Deployment.LicenseTier)
            {
                _disabled.Add($"{module.Name} (license {cfg.LicenseTier} > deployment {configuration.Deployment.LicenseTier})");
                continue;
            }

            _enabled.Add(module);
        }
    }

    public bool IsEnabled(string moduleName) =>
        _enabled.Any(m => m.Name.Equals(moduleName, StringComparison.OrdinalIgnoreCase));

    // ==================================================================
    // Static bootstrap helpers used from Program.cs
    // ==================================================================

    /// <summary>
    /// Loads modules.json into a strongly-typed object.
    /// </summary>
    public static ModulesRoot LoadConfiguration(IWebHostEnvironment env)
    {
        var path = Path.Combine(env.ContentRootPath, "modules.json");
        if (!File.Exists(path))
            throw new FileNotFoundException($"modules.json not found at {path}", path);

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };
        return JsonSerializer.Deserialize<ModulesRoot>(json, options)
            ?? throw new InvalidOperationException("modules.json parsed to null.");
    }

    /// <summary>
    /// Reflects the executing assembly for IModule implementations and instantiates them.
    /// </summary>
    public static IEnumerable<IModule> DiscoverModules()
    {
        return Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => typeof(IModule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
            .Select(t => (IModule)Activator.CreateInstance(t)!)
            .ToList();
    }
}
