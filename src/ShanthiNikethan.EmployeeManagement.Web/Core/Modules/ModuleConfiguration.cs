namespace ShanthiNikethan.EmployeeManagement.Core.Modules;

/// <summary>
/// Represents the parsed contents of modules.json.
/// </summary>
public class ModulesRoot
{
    public DeploymentConfig Deployment { get; set; } = new();
    public Dictionary<string, ModuleConfig> Modules { get; set; } = new();
}

public class DeploymentConfig
{
    public LicenseTier LicenseTier { get; set; } = LicenseTier.Full;
}

public class ModuleConfig
{
    public bool Enabled { get; set; }
    public LicenseTier LicenseTier { get; set; } = LicenseTier.Base;
    public string? Description { get; set; }
}

/// <summary>
/// Ordered from lowest to highest. Base &lt; Standard &lt; Full.
/// A module runs only if its tier &lt;= deployment tier.
/// </summary>
public enum LicenseTier
{
    Base = 0,
    Standard = 1,
    Full = 2
}
