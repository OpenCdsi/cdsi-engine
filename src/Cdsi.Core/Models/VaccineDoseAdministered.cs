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
}
