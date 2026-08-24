/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Contracts;

/// <summary>The full response body for POST /api/v1/forecast - one entry per vaccine group.</summary>
public sealed class ForecastResponseDto
{
    public required string PatientId { get; init; }
    public required DateOnly AssessmentDate { get; init; }
    public required IReadOnlyList<VaccineGroupForecastDto> VaccineGroupForecasts { get; init; }
}

/// <summary>
/// Maps one-to-one from Cdsi.Core.Pipeline.VaccineGroupForecastResult - see ResponseMapping.
/// Enums are represented as their string names (e.g. "NotComplete", "SingleAntigen") rather than
/// numeric values, for a JSON API a real EHR integration will actually read by hand while
/// debugging - see Program.cs's JSON options for the same reasoning applied to Gender in the
/// request direction.
/// </summary>
public sealed class VaccineGroupForecastDto
{
    public required string VaccineGroupName { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; }
    public required bool ShouldForecast { get; init; }

    public DateOnly? EarliestDate { get; init; }
    public DateOnly? AdjustedRecommendedDate { get; init; }
    public DateOnly? AdjustedPastDueDate { get; init; }
    public DateOnly? LatestDate { get; init; }
    public DateOnly? UnadjustedRecommendedDate { get; init; }
    public DateOnly? UnadjustedPastDueDate { get; init; }
    public int? ForecastDoseNumber { get; init; }

    public IReadOnlyList<string> RecommendedVaccineCvxCodes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AllPreferableVaccineCvxCodes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}
