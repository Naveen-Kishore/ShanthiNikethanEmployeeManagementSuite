using System.ComponentModel.DataAnnotations;

namespace ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;

/// <summary>
/// One entry in the admin-manageable list of sub-designations available under
/// a given <see cref="StaffDesignation"/> category (e.g. "Driver" under
/// NonTeaching). New entries can be added from the Staff Profile UI without
/// a code change or redeploy — see <see cref="Services.ISubDesignationService"/>.
/// </summary>
public class SubDesignationOption
{
    public int Id { get; set; }
    public StaffDesignation Category { get; set; }
    [Required, MaxLength(50)] public string Name { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsActive { get; set; } = true;
}
