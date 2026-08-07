namespace ShanthiNikethan.EmployeeManagement.Modules.Admin.Data;

public record PermissionDefinition(string Key, string DisplayName, string Category, string? Description = null);

/// <summary>
/// The full catalog of fine-grained permissions the app actually checks
/// for, anywhere. This is deliberately a fixed, code-defined list, not a
/// database table — each key corresponds to a real enforcement point
/// somewhere in the code, so a new permission can't be "invented" through
/// the UI without a matching code change to actually honor it. What IS
/// fully data-driven and admin-editable is which of these get bundled
/// into which Role Group — that's the RoleGroup/RoleGroupPermission
/// tables, built for exactly that flexibility.
/// </summary>
public static class PermissionCatalog
{
    public static readonly List<PermissionDefinition> All = new()
    {
        // ---- Dashboard ----
        new("Dashboard.View", "View Dashboard Overview", "Dashboard"),
        new("Dashboard.ViewFinancials", "View Dashboard Financial Data", "Dashboard",
            "Payroll cost, EPF/ESIC contribution totals. Without this, Dashboard shows a slimmed-down view."),

        // ---- Staff Directory ----
        new("StaffDirectory.View", "View Staff Directory", "Staff Directory"),
        new("StaffDirectory.ViewFinancials", "View Staff Salary Details", "Staff Directory",
            "Gross pay, net pay, EPF/ESIC amounts on each staff profile."),
        new("StaffDirectory.Edit", "Edit Staff Directory Records", "Staff Directory",
            "Add, edit, or remove staff records."),

        // ---- Payroll ----
        new("Payroll.View", "View Full Payroll Module", "Payroll",
            "Full access to payroll runs, all staff. Regular Staff never gets this — see 'View Own Payslip Only' instead."),
        new("Payroll.Manage", "Manage Payroll Runs", "Payroll",
            "Create, edit, and publish payroll runs."),
        new("Payroll.ViewOwnPayslip", "View Own Payslip Only", "Payroll",
            "Self-service — shows only the signed-in person's own linked staff record, nothing else."),

        // ---- Leave Management ----
        new("Leave.View", "View Leave Management", "Leave Management"),
        new("Leave.Manage", "Manage All Leave Records", "Leave Management",
            "Add, edit, or delete leave records for anyone."),
        new("Leave.ViewOwn", "View Own Leave Records", "Leave Management",
            "Self-service — shows only the signed-in person's own leave history."),

        // ---- Attendance ----
        new("Attendance.View", "View Attendance Module", "Attendance"),
        new("Attendance.Mark", "Mark Staff Attendance", "Attendance",
            "Mark attendance for any staff member, for today only."),
        new("Attendance.AdminOverride", "Correct Past Attendance Entries", "Attendance",
            "Edit a previous day's attendance — the admin-override path around the same-day edit lock."),
        new("Attendance.ViewOwn", "View Own Attendance Record", "Attendance",
            "Self-service — shows only the signed-in person's own attendance history."),

        // ---- Access Management ----
        new("Admin.ManageUsers", "Manage Member Accounts", "Access Management",
            "Create and edit member accounts, assign role groups."),
        new("Admin.ManageRoleGroups", "Manage Role Groups", "Access Management",
            "Create and edit role groups and which roles they bundle together."),
    };

    public static PermissionDefinition? Find(string key) => All.FirstOrDefault(p => p.Key == key);
}
