/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Core.Evaluation;

/// <summary>
/// §9.1's remaining aggregation rules (FORECASTVG-1, 7, 8, 9) - which patient series forecasts
/// belong in a vaccine group's merged forecast, and what gets collected from them once they do.
///
/// FORECASTVG-7 ("the forecast reasons... must be the forecast reasons of all the patient
/// series forecasts contained") needs no function here, same as §8.7's SELECTBEST-1 needed
/// none: it's a plain collection of each contained forecast's own Reason string, not a decision.
///
/// All three functions below are deliberately pure predicates/aggregations over pre-resolved
/// facts (is this series the best one, does its series group match, does its antigen belong to
/// the vaccine group, etc.) rather than something that itself walks a patient's full relevant-series
/// set - that walk is the same deferred orchestration piece noted throughout §8 and §9: every
/// individual rule is built and tested, the end-to-end wiring is separate, larger work.
/// </summary>
public static class VaccineGroupForecastAggregation
{
    /// <summary>FORECASTVG-1: a patient series forecast is contained in a vaccine group's merged forecast only if all three conditions hold - the underlying series is a best patient series (§8.8), it belongs to the specific series group this vaccine group forecast is being built for, and it defines the regimen for an antigen the vaccine group actually classifies.</summary>
    public static bool IsContainedInVaccineGroupForecast(bool isBestPatientSeries, bool seriesGroupMatches, bool antigenBelongsToVaccineGroup) =>
        isBestPatientSeries && seriesGroupMatches && antigenBelongsToVaccineGroup;

    /// <summary>FORECASTVG-8: an antigen is "recommended" for a vaccine group forecast if its best patient series' forecast is contained in the group forecast AND that series' status is 'Not Complete' (i.e. there's actually still something to recommend, not Complete/Immune/Contraindicated/etc.).</summary>
    public static bool IsRecommendedAntigen(bool isContainedInVaccineGroupForecast, PatientSeriesStatus bestPatientSeriesStatus) =>
        isContainedInVaccineGroupForecast && bestPatientSeriesStatus == PatientSeriesStatus.NotComplete;

    /// <summary>FORECASTVG-9: the vaccine group forecast's recommended series dose vaccines are the union of every CONTAINED patient series forecast's own recommended vaccines (§7.5's DetermineRecommendedVaccine output, CVX codes) - forecasts NOT contained (per FORECASTVG-1) don't contribute, even if they'd otherwise have recommended vaccines of their own.</summary>
    public static IReadOnlyList<string> RecommendedSeriesDoseVaccines(
        IReadOnlyList<(bool IsContained, IReadOnlyList<string> RecommendedVaccineCvxCodes)> containedCandidates) =>
        containedCandidates
            .Where(c => c.IsContained)
            .SelectMany(c => c.RecommendedVaccineCvxCodes)
            .Distinct()
            .ToArray();
}
