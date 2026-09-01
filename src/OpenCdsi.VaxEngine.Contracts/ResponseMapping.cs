/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Pipeline;

namespace OpenCdsi.VaxEngine.Contracts;

public static class ResponseMapping
{
    public static ForecastResponseDto ToResponse(
        string patientId, DateOnly assessmentDate, IReadOnlyList<VaccineGroupForecastResult> results) => new()
    {
        PatientId = patientId,
        AssessmentDate = assessmentDate,
        VaccineGroupForecasts = results.Select(ToDto).OrderBy(r => r.VaccineGroupName, StringComparer.Ordinal).ToArray()
    };

    private static VaccineGroupForecastDto ToDto(VaccineGroupForecastResult result) => new()
    {
        VaccineGroupName = result.VaccineGroupName,
        Type = result.Type.ToString(),
        Status = result.Status.ToString(),
        ShouldForecast = result.ShouldForecast,
        EarliestDate = result.EarliestDate,
        AdjustedRecommendedDate = result.AdjustedRecommendedDate,
        AdjustedPastDueDate = result.AdjustedPastDueDate,
        LatestDate = result.LatestDate,
        UnadjustedRecommendedDate = result.UnadjustedRecommendedDate,
        UnadjustedPastDueDate = result.UnadjustedPastDueDate,
        ForecastDoseNumber = result.ForecastDoseNumber,
        RecommendedVaccineCvxCodes = result.RecommendedVaccineCvxCodes,
        AllPreferableVaccineCvxCodes = result.AllPreferableVaccineCvxCodes,
        Reasons = result.Reasons
    };
}
