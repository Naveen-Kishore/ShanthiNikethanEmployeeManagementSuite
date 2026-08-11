namespace ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;

/// <summary>
/// Which permission(s) make a module's nav item visible at all. Having
/// ANY listed permission is enough — e.g. Payroll shows up for someone
/// with either full access or just their own payslip. Kept as a small
/// standalone map rather than a new IModule property, so this didn't
/// require touching every existing module file just to add the concept.
/// </summary>
public static class ModuleAccessMap
{
    private static readonly Dictionary<string, string[]> RequiredAnyOf = new()
    {
        ["Dashboard"] = new[] { "Dashboard.View" },
        ["StaffProfile"] = new[] { "StaffDirectory.View" },
        ["Attendance"] = new[] { "Attendance.View", "Attendance.ViewOwn" },
        ["Leave"] = new[] { "Leave.View", "Leave.ViewOwn" },
        ["Payroll"] = new[] { "Payroll.View", "Payroll.ViewOwnPayslip" },
        ["Admin"] = new[] { "Admin.ManageUsers", "Admin.ManageRoleGroups" },
        ["IdentityProvider"] = new[] { "Admin.ManageUsers", "Admin.ManageRoleGroups" },
        ["AuditLog"] = new[] { "Admin.ViewAuditLog" },
        ["AutomationRules"] = new[] { "Admin.ManageAutomationRules" },
    };

    /// <summary>True if the user has at least one of the module's required permissions. Fail-closed: a module not listed here at all is hidden, not shown - the safer default for a system built to protect financial data. Global Administrator already holds every permission in the catalog, so this never blocks that role; it only affects a module someone forgot to map after adding it.</summary>
    public static bool CanView(string moduleName, Func<string, bool> hasPermission)
    {
        if (!RequiredAnyOf.TryGetValue(moduleName, out var perms)) return false;
        return perms.Any(hasPermission);
    }
}
