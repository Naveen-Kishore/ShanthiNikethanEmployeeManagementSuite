namespace ShanthiNikethan.EmployeeManagement.Modules.Payroll.Data;

/// <summary>
/// One monthly payroll cycle. Starts as a Draft (created from a live
/// snapshot of Staff Directory); once Published, it and its line items
/// become immutable — the numbers you filed with the education department
/// must never silently change even if someone's current salary changes
/// next month. This is why Payroll snapshots data rather than referencing
/// Staff records live.
///
/// A month can have more than one run — a Regular Salary run paid on the
/// 1st, and a separate Performance Incentive or Pongal Bonus run paid
/// mid-month to a subset of staff. They're independent runs, not one run
/// with extra line items, specifically so a bonus run never touches
/// Base Net Pay and can't accidentally double-pay salary.
/// </summary>
public class PayrollRun
{
    public Guid Id { get; set; }
    public int Year { get; set; }
    public int Month { get; set; } // 1-12
    public PayrollRunStatus Status { get; set; } = PayrollRunStatus.Draft;
    public PayrollRunType RunType { get; set; } = PayrollRunType.RegularSalary;

    /// <summary>Only used when RunType is Other — the custom label for a payment type not covered by the fixed list.</summary>
    public string? OtherLabel { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public string CreatedByObjectId { get; set; } = string.Empty;
    public string CreatedByDisplayName { get; set; } = string.Empty;

    public DateTime? PublishedAtUtc { get; set; }
    public string? PublishedByObjectId { get; set; }
    public string? PublishedByDisplayName { get; set; }

    public string MonthLabel => new DateOnly(Year, Month, 1).ToString("MMMM yyyy");

    /// <summary>Human label used consistently in the PDF title and the CSV bank narration, e.g. "Salary", "Performance Incentive", "Pongal Bonus".</summary>
    public string RunTypeLabel => RunType switch
    {
        PayrollRunType.RegularSalary => "Salary",
        PayrollRunType.PerformanceIncentive => "Performance Incentive",
        PayrollRunType.SpecialClassAllowance => "Special Class Allowance",
        PayrollRunType.PongalBonus => "Pongal Bonus",
        PayrollRunType.Other => string.IsNullOrWhiteSpace(OtherLabel) ? "Payment" : OtherLabel,
        _ => "Payment"
    };

    public bool IsRegularSalary => RunType == PayrollRunType.RegularSalary;
}

public enum PayrollRunStatus
{
    Draft,
    Published
}

public enum PayrollRunType
{
    RegularSalary,
    PerformanceIncentive,
    SpecialClassAllowance,
    PongalBonus,
    Other
}
