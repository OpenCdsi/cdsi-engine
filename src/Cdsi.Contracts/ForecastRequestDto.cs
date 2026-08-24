/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Contracts;

/// <summary>
/// The full request body for POST /api/v1/forecast. Every field here maps directly to a field
/// on Cdsi.Core.Models.Patient/VaccineDoseAdministered - see RequestMapping for the exact
/// mapping, including which optional fields default to what.
/// </summary>
public sealed class ForecastRequestDto
{
    public required string PatientId { get; init; }
    public required DateOnly DateOfBirth { get; init; }

    /// <summary>"Male", "Female", or "Unknown" (case-insensitive). Omit or null defaults to "Unknown" - per Table 5-2's own "Assumed Value if Empty," never default to a specific gender.</summary>
    public string? Gender { get; init; }

    public string? CountryOfBirth { get; init; }

    /// <summary>Omit or null defaults to today (server date) at request time.</summary>
    public DateOnly? AssessmentDate { get; init; }

    public IReadOnlyList<PatientObservationDto>? ActiveObservations { get; init; }
    public IReadOnlyList<PatientObservationDto>? AdverseReactions { get; init; }

    /// <summary>See Patient.UnresolvedObservationCodes' own doc comment for why this exists and matters - an observation code listed here is treated as "Unknown" for indication matching, not silently as "No."</summary>
    public IReadOnlyList<string>? UnresolvedObservationCodes { get; init; }

    public IReadOnlyList<AdministeredDoseDto>? AdministeredDoses { get; init; }
}

public sealed class PatientObservationDto
{
    public required string Code { get; init; }
    public string? Text { get; init; }
    public DateOnly? ObservationDate { get; init; }
}

public sealed class AdministeredDoseDto
{
    public required string DoseId { get; init; }
    public required string Cvx { get; init; }
    public required DateOnly DateAdministered { get; init; }
    public string? TradeName { get; init; }
    public double? Volume { get; init; }
    public DateOnly? LotExpirationDate { get; init; }
    public bool DoseConditionFlag { get; init; }
}
