/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Pipeline;

/// <summary>
/// Everything GeneratePatientForecast.Execute produces, plus per-dose evaluation detail for the
/// winning series per antigen - added for Cdsi.Conformance.Tests, which needs to check
/// individual administered-dose outcomes (Valid/NotValid/Reason per §6, not just the merged
/// §9 vaccine-group-level forecast). The original Execute(...) overload below is unchanged and
/// still returns just VaccineGroupForecasts - every existing caller (Cdsi.Api, Cdsi.Functions,
/// Cdsi.Demo) that doesn't need per-dose detail is unaffected.
/// </summary>
public sealed class PatientForecastResult
{
    public required IReadOnlyList<VaccineGroupForecastResult> VaccineGroupForecasts { get; init; }

    /// <summary>
    /// Keyed by antigen name (matches AntigenSeries.Antigen) - the WINNING (§8-selected best)
    /// patient series' own dose-by-dose evaluation detail. An antigen with no relevant series at
    /// all simply has no entry here.
    ///
    /// SIMPLIFICATION, flagged: §8.8 can legitimately select more than one "best" series for the
    /// same antigen at once (a real, documented scenario elsewhere in this project - e.g. two
    /// equivalent HepB series groups both independently Complete). This dictionary holds only
    /// one SeriesHistoryResult per antigen, so in that rare case whichever series is processed
    /// last wins - a real, deliberate simplification for a genuinely rare edge case, not an
    /// oversight, matching how other multi-winner scenarios have been handled elsewhere in this
    /// project (see EvaluatePatientSeriesHistory's own "only contribute once per antigen" note).
    /// </summary>
    public required IReadOnlyDictionary<string, SeriesHistoryResult> DoseDetailsByAntigen { get; init; }
}

/// <summary>
/// The complete, end-to-end pipeline: raw administered doses in, merged vaccine group forecasts
/// out. Wires together everything this project has built - §4.2/§5.1 (organize history, find
/// relevant series), §4.4/§6 (evaluate immunization history per series, via
/// EvaluatePatientSeriesHistory), §7 (forecast per series, via GeneratePatientSeriesForecast),
/// §8 (select best patient series per series group, then per antigen, via
/// SelectPrioritizedPatientSeriesForGroup and DetermineBestPatientSeriesForAntigen), and §9
/// (merge into vaccine group forecasts, via MergeVaccineGroupForecast) - into one call.
///
/// Immunity and contraindication reference data are loaded per antigen FILE (not part of
/// AntigenSeries itself), so they're supplied here as dictionaries keyed by antigen name
/// (matching AntigenSeries.Antigen) rather than re-loaded internally.
///
/// §6.2's "Completed Series" condition is now resolved for real, via two evaluation passes -
/// see ResolveCompletedSeriesGroups for why that converges correctly for every real instance in
/// the dataset. This is why `resolveCompletedSeries` is no longer part of this function's public
/// signature at all: callers shouldn't need to know this internal mechanism exists.
///
/// REMAINING KNOWN GAPS, consistent with every other round this project has flagged rather than
/// guessed past: §7.5's `latestConflictEndDate`/`latestInadvertentAdministrationDate` remain
/// unset (null) for every series - the forward-looking calculations they'd need don't exist yet.
/// §9.3's multi-antigen `anyContainedIsPriorityForecast`/`latestAdministeredDateOfGroupVaccineTypes`
/// default to false/null for the same reason. Neither gap affects the (much more common)
/// single-antigen vaccine groups or non-priority multi-antigen cases at all.
/// </summary>
public static class GeneratePatientForecast
{
    /// <summary>The original, stable public entry point - unchanged. Still just merged vaccine group forecasts, for callers that don't need per-dose detail.</summary>
    public static IReadOnlyList<VaccineGroupForecastResult> Execute(
        Patient patient,
        IReadOnlyList<VaccineDoseAdministered> administeredDoses,
        IReadOnlyList<AntigenSeries> allSeries,
        ScheduleSupportingData schedule,
        IReadOnlyList<VaccineGroupInfo> vaccineGroups,
        IReadOnlyDictionary<string, AntigenImmunityData> immunityByAntigen,
        IReadOnlyDictionary<string, AntigenContraindicationData> contraindicationsByAntigen,
        DateOnly assessmentDate) =>
        ExecuteWithDoseDetail(
            patient, administeredDoses, allSeries, schedule, vaccineGroups,
            immunityByAntigen, contraindicationsByAntigen, assessmentDate).VaccineGroupForecasts;

    /// <summary>Same computation as Execute, plus per-antigen dose-by-dose detail for the winning series - see PatientForecastResult.</summary>
    public static PatientForecastResult ExecuteWithDoseDetail(
        Patient patient,
        IReadOnlyList<VaccineDoseAdministered> administeredDoses,
        IReadOnlyList<AntigenSeries> allSeries,
        ScheduleSupportingData schedule,
        IReadOnlyList<VaccineGroupInfo> vaccineGroups,
        IReadOnlyDictionary<string, AntigenImmunityData> immunityByAntigen,
        IReadOnlyDictionary<string, AntigenContraindicationData> contraindicationsByAntigen,
        DateOnly assessmentDate)
    {
        // §5.1: which series even apply to this patient (gender, etc.)
        var relevantSeries = CreateRelevantPatientSeries.Execute(patient, allSeries, assessmentDate).RelevantSeries;

        // Pass 1: evaluate assuming no series group is complete yet, purely to discover which
        // ones actually are (SeriesHistoryResult.SeriesComplete) - §6.2's Completed Series
        // condition needs this before it can be resolved for real.
        var firstPassHistory = EvaluatePatientSeriesHistory.Execute(
            patient, relevantSeries, administeredDoses, schedule.CvxToAntigen, schedule.ConflictsByImpactedCvx,
            resolveCompletedSeries: (_, _) => false);
        var resolveCompletedSeries = ResolveCompletedSeriesGroups.Build(firstPassHistory);

        // Pass 2 (authoritative): §4.2/§4.4/§6, now with the real Completed Series resolver,
        // plus the real cross-antigen vaccine conflict resolution EvaluatePatientSeriesHistory
        // already wires in.
        var historyBySeries = EvaluatePatientSeriesHistory.Execute(
            patient, relevantSeries, administeredDoses, schedule.CvxToAntigen, schedule.ConflictsByImpactedCvx, resolveCompletedSeries);

        // Patient-wide evaluated-dose history, one antigen's worth per antigen (matching
        // EvaluatePatientSeriesHistory's own "only contribute once per antigen" simplification -
        // see its doc comment), needed for cross-antigen forecast conflict resolution
        // (CALCDTCONFLICT-3) the same way EvaluatePatientSeriesHistory itself needs it for §6.7.
        var patientWideHistoryByAntigen = new Dictionary<string, IReadOnlyList<EvaluatedAntigenDose>>();
        foreach (var (series, history) in historyBySeries)
        {
            if (!patientWideHistoryByAntigen.ContainsKey(series.Antigen))
            {
                patientWideHistoryByAntigen[series.Antigen] = history.AllEvaluatedDoses;
            }
        }

        // §7: forecast each series individually, grouping the results by antigen for §8.
        var membersByAntigen = new Dictionary<string, List<SeriesGroupMember>>();
        foreach (var (series, history) in historyBySeries)
        {
            if (!immunityByAntigen.TryGetValue(series.Antigen, out var immunity) ||
                !contraindicationsByAntigen.TryGetValue(series.Antigen, out var contraindications))
            {
                throw new InvalidOperationException($"No immunity/contraindication data supplied for antigen '{series.Antigen}'.");
            }

            var priorDosesAllAntigens = patientWideHistoryByAntigen
                .Where(kv => kv.Key != series.Antigen)
                .SelectMany(kv => kv.Value)
                .Select(EvaluateDoseAgainstTargetDose.MapToPriorDoseForSkipOrConflict)
                .ToArray();

            var forecast = GeneratePatientSeriesForecast.Execute(
                patient, series, history, assessmentDate, immunity, contraindications,
                priorDosesAllAntigens, schedule.ConflictsByImpactedCvx,
                groups => resolveCompletedSeries(series.Antigen, groups));

            if (!membersByAntigen.TryGetValue(series.Antigen, out var members))
            {
                members = new List<SeriesGroupMember>();
                membersByAntigen[series.Antigen] = members;
            }
            members.Add(new SeriesGroupMember(series, history, forecast));
        }

        // §8: best patient series per antigen (across that antigen's own series groups), paired
        // with the forecast already computed for it above.
        var bestSeriesByVaccineGroup = new Dictionary<string, List<(AntigenSeries Series, PatientSeriesForecastResult Forecast)>>();
        var doseDetailsByAntigen = new Dictionary<string, SeriesHistoryResult>();
        foreach (var members in membersByAntigen.Values)
        {
            var bestSeries = DetermineBestPatientSeriesForAntigen.Execute(members, patient.DateOfBirth, assessmentDate);
            foreach (var series in bestSeries)
            {
                var member = members.First(m => m.Series == series);

                // Recorded regardless of whether this antigen belongs to a vaccine group - a
                // conformance check on an individual dose's Valid/NotValid outcome doesn't
                // depend on §9 merge eligibility. See PatientForecastResult's own doc comment
                // for the (rare, documented) multi-winner-per-antigen simplification this
                // last-write-wins assignment represents.
                doseDetailsByAntigen[series.Antigen] = member.SeriesHistory;

                if (series.VaccineGroup is null)
                {
                    continue; // no vaccine group classifies this antigen - nothing to merge into
                }
                if (!bestSeriesByVaccineGroup.TryGetValue(series.VaccineGroup, out var list))
                {
                    list = new List<(AntigenSeries, PatientSeriesForecastResult)>();
                    bestSeriesByVaccineGroup[series.VaccineGroup] = list;
                }
                list.Add((series, member.Forecast));
            }
        }

        // §9: merge each vaccine group's contained best-series forecasts into one result.
        var results = new List<VaccineGroupForecastResult>();
        foreach (var (vaccineGroupName, contained) in bestSeriesByVaccineGroup)
        {
            var antigensInGroup = allSeries.Where(s => s.VaccineGroup == vaccineGroupName).Select(s => s.Antigen).Distinct().ToArray();
            var type = VaccineGroupClassification.Classify(antigensInGroup);
            var administerFull = vaccineGroups.FirstOrDefault(v => v.Name == vaccineGroupName)?.AdministerFullVaccineGroup ?? false;

            // §9.3 MULTIANTVG-1's two remaining inputs, only meaningful for multi-antigen groups
            // (MMR, DTaP/Tdap/Td in real data) - harmless to compute for single-antigen groups
            // too, since SingleAntigenVaccineGroup.EarliestDate never reads them.
            var anyContainedIsPriorityForecast = contained.Any(x => x.Forecast.IsPriorityForecast);
            var latestAdministeredDateOfGroupVaccineTypes = antigensInGroup
                .Where(patientWideHistoryByAntigen.ContainsKey)
                .SelectMany(antigen => patientWideHistoryByAntigen[antigen])
                .Select(d => (DateOnly?)d.DateAdministered)
                .Max();

            results.Add(MergeVaccineGroupForecast.Execute(
                vaccineGroupName, type, administerFull, contained,
                anyContainedIsPriorityForecast, latestAdministeredDateOfGroupVaccineTypes));
        }

        return new PatientForecastResult
        {
            VaccineGroupForecasts = results,
            DoseDetailsByAntigen = doseDetailsByAntigen
        };
    }
}
