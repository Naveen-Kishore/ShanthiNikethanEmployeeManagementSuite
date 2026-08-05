using ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Data;

namespace ShanthiNikethan.EmployeeManagement.Modules.Payroll.Data;

/// <summary>
/// A frozen snapshot of one staff member's payable amount for a specific
/// PayrollRun. Deliberately duplicates data already on the Staff record
/// (name, account number, designation) rather than referencing it live —
/// that duplication is the whole point of a payroll snapshot. StaffId is
/// kept only for traceability back to the profile, never for live lookups
/// once the run exists.
/// </summary>
public class PayrollLineItem
{
    public Guid Id { get; set; }
    public Guid PayrollRunId { get; set; }

    public Guid StaffId { get; set; }
    public string StaffCode { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public StaffDesignation Designation { get; set; }
    public string BankAccountNumber { get; set; } = string.Empty;
    public BankPaymentMode BankMode { get; set; }
    public decimal NetPay { get; set; }
}
