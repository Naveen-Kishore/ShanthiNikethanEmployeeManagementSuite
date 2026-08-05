namespace ShanthiNikethan.EmployeeManagement.Modules.StaffProfile.Services;

/// <summary>
/// Computes statutory salary components from Gross Pay according to
/// Indian labour law (EPF Act 1952, ESIC Act 1948).
///
/// Basic Wage = 50% of Gross Pay (standard EPFO convention, verified against
/// the school's actual June 2026 EPFO ECR contribution report). EPF-related
/// figures (Basic Wage, Employee EPF, Employer EPS, Employer EPF, EDLI) are
/// rounded to the nearest WHOLE RUPEE, matching the ECR portal's own
/// rounding. ESIC and Net/Gross Pay remain at 2-decimal (paise) precision.
///
/// EPF and ESIC enrollment are both caller-supplied booleans, not computed
/// here. In practice, callers derive them from <c>Staff.IsEpfEnabled</c> and
/// <c>Staff.IsEsicEnabled</c> — which are themselves derived from whether an
/// EPF UAN / ESIC number is on file, rather than a separate toggle. This
/// class stays agnostic to where the flags come from.
///
/// Note on ESIC: this deliberately does NOT auto-derive eligibility from the
/// ₹21,000 wage threshold. Per ESIC rules, once enrolled, contribution
/// continues for the rest of the current contribution period even if wages
/// rise above the threshold mid-period — so enrollment is a one-time admin
/// decision (reflected by adding/removing the ESIC number), not something
/// that should silently flip off the moment a raise crosses ₹21,000.
///
/// All calculations are pure functions with no state — safe to use from
/// any thread and safe to call from Blazor UI on every keystroke.
///
/// Statutory rules encoded (when EPF-enabled):
///   * Basic Wage = 50% of Gross Pay                              [rounded to whole rupee]
///   * Employee EPF = 12% of Basic Wage                           [rounded to whole rupee]
///   * Employer EPS (Pension) = 8.33% of min(Basic Wage, ₹15,000) [rounded to whole rupee, statutory cap]
///   * Employer EPF = (12% of Basic Wage) − Employer EPS          [rounded to whole rupee, balance]
///   * EDLI & Admin charges = 1% of min(Basic Wage, ₹15,000)      [rounded to whole rupee, same wage cap as EPS]
///
/// Statutory rules encoded (when ESIC-enabled):
///   * Employee ESIC = 0.75% of Gross Pay
///   * Employer ESIC = 3.25% of Gross Pay
///
/// Always: Net Pay = Gross Pay − Employee EPF − Employee ESIC (when not overridden)
///
/// If any of these percentages change in future budget announcements, edit
/// the constants in this file. No data migration is needed because we never
/// store the derived values.
/// </summary>
public class StatutorySalaryCalculator
{
    // === Statutory constants — update here if government rules change ===
    public const decimal BasicWageRatio         = 0.50m;
    public const decimal EmployeeEpfRate        = 0.12m;
    public const decimal EmployerEpsRate        = 0.0833m;
    public const decimal EpsBasicWageCap        = 15000m;    // ₹15,000 statutory pension cap
    public const decimal EdliAndAdminRate       = 0.01m;
    public const decimal EmployeeEsicRate       = 0.0075m;
    public const decimal EmployerEsicRate       = 0.0325m;

    /// <summary>
    /// Informational only — NOT used to gate ESIC calculation (see class
    /// remarks). Useful for a UI hint like "Gross has crossed ₹21,000 —
    /// contribution continuing is expected mid-period."
    /// </summary>
    public const decimal EsicGrossPayThreshold  = 21000m;

    public StatutoryBreakdown Compute(decimal grossPay, bool isEpfEnabled, bool isEsicEnabled, decimal? netPayOverride = null)
    {
        if (grossPay < 0) grossPay = 0;

        var empEsic = isEsicEnabled ? Round(grossPay * EmployeeEsicRate) : 0m;
        var employerEsic = isEsicEnabled ? Round(grossPay * EmployerEsicRate) : 0m;

        decimal basic = 0, empEpf = 0, employerEps = 0, employerEpf = 0, edli = 0;
        if (isEpfEnabled)
        {
            basic = RoundRupee(grossPay * BasicWageRatio);
            empEpf = RoundRupee(basic * EmployeeEpfRate);
            employerEps = RoundRupee(Math.Min(basic, EpsBasicWageCap) * EmployerEpsRate);
            employerEpf = RoundRupee((basic * EmployeeEpfRate) - employerEps);
            if (employerEpf < 0) employerEpf = 0;
            edli = RoundRupee(Math.Min(basic, EpsBasicWageCap) * EdliAndAdminRate);
        }

        var computedNet = grossPay - empEpf - empEsic;
        var netPay = netPayOverride ?? computedNet;

        return new StatutoryBreakdown(
            GrossPay: grossPay,
            BasicWage: basic,
            EmployeeEpf: empEpf,
            EmployeeEsic: empEsic,
            EmployerEps: employerEps,
            EmployerEpf: employerEpf,
            EdliAndAdmin: edli,
            EmployerEsic: employerEsic,
            NetPayComputed: computedNet,
            NetPay: netPay,
            IsNetPayOverridden: netPayOverride.HasValue,
            EsicApplicable: isEsicEnabled,
            IsEpfEnabled: isEpfEnabled,
            GrossOverEsicThreshold: grossPay > EsicGrossPayThreshold
        );
    }

    /// <summary>
    /// Inverse of <see cref="Compute"/>: given a target Net Pay, solves for the Gross
    /// Pay that produces it. Since EPF/ESIC enrollment are now known inputs (not
    /// something to guess from Gross), this is a single direct calculation — no
    /// branching needed, unlike the old threshold-guessing version.
    /// </summary>
    public decimal InverseGrossFromNet(decimal netPay, bool isEpfEnabled, bool isEsicEnabled)
    {
        if (netPay <= 0) return 0;

        var deductionRate = 0m;
        if (isEpfEnabled) deductionRate += BasicWageRatio * EmployeeEpfRate; // 6% of Gross, since EPF = 12% of 50% of Gross
        if (isEsicEnabled) deductionRate += EmployeeEsicRate;

        return Round(netPay / (1 - deductionRate));
    }

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);

    /// <summary>Rounds to the nearest whole rupee — matches EPFO ECR portal convention.</summary>
    private static decimal RoundRupee(decimal value) => Math.Round(value, 0, MidpointRounding.AwayFromZero);
}

/// <summary>
/// Read-only value object with the full salary breakdown for one staff member.
/// Bound to the profile drawer UI for real-time display.
/// </summary>
public record StatutoryBreakdown(
    decimal GrossPay,
    decimal BasicWage,
    decimal EmployeeEpf,
    decimal EmployeeEsic,
    decimal EmployerEps,
    decimal EmployerEpf,
    decimal EdliAndAdmin,
    decimal EmployerEsic,
    decimal NetPayComputed,
    decimal NetPay,
    bool IsNetPayOverridden,
    bool EsicApplicable,
    bool IsEpfEnabled,
    bool GrossOverEsicThreshold
)
{
    public decimal TotalEmployeeDeduction => EmployeeEpf + EmployeeEsic;
    public decimal TotalEmployerContribution => EmployerEps + EmployerEpf + EdliAndAdmin + EmployerEsic;
    public decimal CostToInstitution => GrossPay + TotalEmployerContribution;
}
