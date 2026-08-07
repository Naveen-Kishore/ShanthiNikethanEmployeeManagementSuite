namespace ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;

/// <summary>
/// A named bundle of permissions, assignable to user accounts. Fully
/// data-driven — created and edited through Administration, not
/// hardcoded. The three seeded defaults (Global Administrator, Office
/// Admin, Regular Staff) are marked IsSystemDefined so they can't be
/// accidentally deleted, but their permission lists can still be edited
/// like any other role group.
/// </summary>
public class RoleGroup
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemDefined { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedByObjectId { get; set; } = string.Empty;
    public string CreatedByDisplayName { get; set; } = string.Empty;

    public List<RoleGroupPermission> Permissions { get; set; } = new();
}

public class RoleGroupPermission
{
    public Guid Id { get; set; }
    public Guid RoleGroupId { get; set; }
    /// <summary>Matches a PermissionDefinition.Key from the fixed PermissionCatalog.</summary>
    public string PermissionKey { get; set; } = string.Empty;
}
