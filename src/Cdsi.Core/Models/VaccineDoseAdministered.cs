namespace Cdsi.Core.Models;

/// <summary>
/// A single administered vaccine as recorded in the patient's immunization history —
/// the raw input to §4.2 Organize Immunization History. One CVX code, one date.
/// Not yet associated with any antigen; that association is what OrganizeImmunizationHistory computes.
/// </summary>
public sealed class VaccineDoseAdministered
{
    public required string DoseId { get; init; }
    public required string Cvx { get; init; }
    public required DateOnly DateAdministered { get; init; }

    /// <summary>Needed by §6.8 Evaluate Preferable Vaccine (Table 6-26), which compares the administered dose's own trade name against the target dose's preferable-vaccine trade name. Absent for ~98.5% of real preferableVaccine entries (they don't specify a trade name to match), so this being null is the common case, not an error.</summary>
    public string? TradeName { get; init; }

    /// <summary>Needed by §6.8 (Table 6-26), which compares the administered volume against the target dose's preferable-vaccine minimum volume.</summary>
    public double? Volume { get; init; }

    /// <summary>Needed by §6.1 (Table 6-2/6-3). Null means unknown/not tracked, which defaults to 12/31/2999 (never expired) per Table 6-2.</summary>
    public DateOnly? LotExpirationDate { get; init; }

    /// <summary>Needed by §6.1 (Table 6-2/6-3) - true if the administered dose record carries a condition flag (misadministration, recall, cold chain breach, etc.).</summary>
    public bool DoseConditionFlag { get; init; }
}
