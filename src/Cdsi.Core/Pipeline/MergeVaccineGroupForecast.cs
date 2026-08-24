/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Pipeline;

/// <summary>The complete §9 merged forecast for one vaccine group.</summary>
public sealed class VaccineGroupForecastResult
{
    public required string VaccineGroupName { get; init; }
    public required VaccineGroupType Type { get; init; }
    public required PatientSeriesStatus Status { get; init; }
    public required bool ShouldForecast { get; init; }

    public DateOnly? EarliestDate { get; init; }
    public DateOnly? AdjustedRecommendedDate { get; init; }
    public DateOnly? AdjustedPastDueDate { get; init; }
    public DateOnly? LatestDate { get; init; }
    public DateOnly? UnadjustedRecommendedDate { get; init; }
    public DateOnly? UnadjustedPastDueDate { get; init; }
    public int? ForecastDoseNumber { get; init; }

    public IReadOnlyList<string> RecommendedVaccineCvxCodes { get; init; } = Array.Empty<string>();

    /// <summary>Union of every contained forecast's AllPreferableVaccineCvxCodes - see PatientSeriesForecastResult's own field for why this exists alongside RecommendedVaccineCvxCodes.</summary>
    public IReadOnlyList<string> AllPreferableVaccineCvxCodes { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}

/// <summary>
/// §9's final merge step: combines every antigen's best patient series forecast (§8.8's output,
/// one call per antigen in the group) into one vaccine group forecast, per §9.1's aggregation
/// rules and §9.2/§9.3's status/earliest-date rules.
///
/// `bestSeriesPerAntigen` is a flat list across every antigen the vaccine group classifies - not
/// necessarily one entry per antigen. A single antigen can itself contribute more than one best
/// patient series (discovered while building this orchestrator: two equivalent series groups for
/// the same antigen can both independently resolve to Complete via §8.8's Column 1). Status
/// resolution (§9.2/§9.3) tolerates that when every contained status agrees; a genuine
/// disagreement is a real inconsistency that surfaces as an exception rather than a guess.
///
/// Date/dose-number aggregation only considers the subset of contained forecasts that are
/// actually forecasting (ShouldForecast=true, Dates not null) - a Complete/Immune/Contraindicated
/// series contributes nothing to date math, matching how a single series' own Complete status
/// means its own Dates are null (§7.4/§7.5).
/// </summary>
public static class MergeVaccineGroupForecast
{
    public static VaccineGroupForecastResult Execute(
        string vaccineGroupName,
        VaccineGroupType type,
        bool administerFullVaccineGroupForDoseNumber,
        IReadOnlyList<(AntigenSeries Series, PatientSeriesForecastResult Forecast)> bestSeriesPerAntigen,
        bool anyContainedIsPriorityForecast = false,
        DateOnly? latestAdministeredDateOfGroupVaccineTypes = null)
    {
        var allStatuses = bestSeriesPerAntigen.Select(x => x.Forecast.Status).ToArray();
        var status = type == VaccineGroupType.SingleAntigen
            ? SingleAntigenVaccineGroup.Status(allStatuses)
            : MultipleAntigenVaccineGroup.Status(allStatuses);

        var forecasting = bestSeriesPerAntigen.Where(x => x.Forecast.ShouldForecast && x.Forecast.Dates is not null).ToArray();

        if (forecasting.Length == 0)
        {
            return new VaccineGroupForecastResult
            {
                VaccineGroupName = vaccineGroupName,
                Type = type,
                Status = status,
                ShouldForecast = false
            };
        }

        var earliestDate = type == VaccineGroupType.SingleAntigen
            ? SingleAntigenVaccineGroup.EarliestDate(forecasting.Select(x => x.Forecast.Dates!.EarliestDate).ToArray())
            : MultipleAntigenVaccineGroup.EarliestDate(
                anyContainedIsPriorityForecast,
                forecasting.Select(x => x.Forecast.Dates!.EarliestDate).ToArray(),
                latestAdministeredDateOfGroupVaccineTypes);

        var adjustedRecommendedDate = VaccineGroupForecastDates.AdjustedRecommendedDate(
            earliestDate, forecasting.Select(x => x.Forecast.Dates!.AdjustedRecommendedDate).ToArray());
        var adjustedPastDueDate = VaccineGroupForecastDates.AdjustedPastDueDate(
            earliestDate, forecasting.Select(x => x.Forecast.Dates!.AdjustedPastDueDate).ToArray());
        var latestDate = VaccineGroupForecastDates.LatestDate(
            forecasting.Select(x => x.Forecast.Dates!.LatestDate).ToArray());
        var unadjustedRecommendedDate = VaccineGroupForecastDates.UnadjustedRecommendedDate(
            forecasting.Select(x => x.Forecast.Dates!.UnadjustedRecommendedDate).ToArray());
        var unadjustedPastDueDate = VaccineGroupForecastDates.UnadjustedPastDueDate(
            forecasting.Select(x => x.Forecast.Dates!.UnadjustedPastDueDate).ToArray());
        var forecastDoseNumber = VaccineGroupForecastDates.ForecastDoseNumber(
            administerFullVaccineGroupForDoseNumber, forecasting.Select(x => x.Forecast.ForecastDoseNumber!.Value).ToArray());

        var recommendedVaccines = VaccineGroupForecastAggregation.RecommendedSeriesDoseVaccines(
            bestSeriesPerAntigen.Select(x => (true, x.Forecast.RecommendedVaccineCvxCodes)).ToArray());

        var allPreferableVaccines = VaccineGroupForecastAggregation.RecommendedSeriesDoseVaccines(
            bestSeriesPerAntigen.Select(x => (true, x.Forecast.AllPreferableVaccineCvxCodes)).ToArray());

        // FORECASTVG-7: plain collection, not a decision - see VaccineGroupForecastAggregation's own doc comment.
        var reasons = bestSeriesPerAntigen.Select(x => x.Forecast.StatusReason).Distinct().ToArray();

        return new VaccineGroupForecastResult
        {
            VaccineGroupName = vaccineGroupName,
            Type = type,
            Status = status,
            ShouldForecast = true,
            EarliestDate = earliestDate,
            AdjustedRecommendedDate = adjustedRecommendedDate,
            AdjustedPastDueDate = adjustedPastDueDate,
            LatestDate = latestDate,
            UnadjustedRecommendedDate = unadjustedRecommendedDate,
            UnadjustedPastDueDate = unadjustedPastDueDate,
            ForecastDoseNumber = forecastDoseNumber,
            RecommendedVaccineCvxCodes = recommendedVaccines,
            AllPreferableVaccineCvxCodes = allPreferableVaccines,
            Reasons = reasons
        };
    }
}
