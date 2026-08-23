using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Pipeline;

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
/// KNOWN GAPS, consistent with every other round this project has flagged rather than guessed
/// past: §7.5's `latestConflictEndDate`/`latestInadvertentAdministrationDate` remain unset
/// (null) for every series - the forward-looking calculations they'd need don't exist yet.
/// §9.3's multi-antigen `anyContainedIsPriorityForecast`/`latestAdministeredDateOfGroupVaccineTypes`
/// default to false/null for the same reason. Neither gap affects the (much more common)
/// single-antigen vaccine groups or non-priority multi-antigen cases at all.
/// </summary>
public static class GeneratePatientForecast
{
    public static IReadOnlyList<VaccineGroupForecastResult> Execute(
        Patient patient,
        IReadOnlyList<VaccineDoseAdministered> administeredDoses,
        IReadOnlyList<AntigenSeries> allSeries,
        ScheduleSupportingData schedule,
        IReadOnlyList<VaccineGroupInfo> vaccineGroups,
        IReadOnlyDictionary<string, AntigenImmunityData> immunityByAntigen,
        IReadOnlyDictionary<string, AntigenContraindicationData> contraindicationsByAntigen,
        DateOnly assessmentDate,
        Func<string?, bool> resolveCompletedSeries)
    {
        // §5.1: which series even apply to this patient (gender, etc.)
        var relevantSeries = CreateRelevantPatientSeries.Execute(patient, allSeries, assessmentDate).RelevantSeries;

        // §4.2/§4.4/§6: evaluate every relevant series' dose history, with real cross-antigen
        // vaccine conflict resolution already wired in by EvaluatePatientSeriesHistory.
        var historyBySeries = EvaluatePatientSeriesHistory.Execute(
            patient, relevantSeries, administeredDoses, schedule.CvxToAntigen, schedule.ConflictsByImpactedCvx, resolveCompletedSeries);

        // §7: forecast each series individually, grouping the results by antigen for §8.
        var membersByAntigen = new Dictionary<string, List<SeriesGroupMember>>();
        foreach (var (series, history) in historyBySeries)
        {
            if (!immunityByAntigen.TryGetValue(series.Antigen, out var immunity) ||
                !contraindicationsByAntigen.TryGetValue(series.Antigen, out var contraindications))
            {
                throw new InvalidOperationException($"No immunity/contraindication data supplied for antigen '{series.Antigen}'.");
            }

            var forecast = GeneratePatientSeriesForecast.Execute(
                patient, series, history, assessmentDate, immunity, contraindications, resolveCompletedSeries);

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
        foreach (var members in membersByAntigen.Values)
        {
            var bestSeries = DetermineBestPatientSeriesForAntigen.Execute(members, patient.DateOfBirth, assessmentDate);
            foreach (var series in bestSeries)
            {
                if (series.VaccineGroup is null)
                {
                    continue; // no vaccine group classifies this antigen - nothing to merge into
                }
                var member = members.First(m => m.Series == series);
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

            results.Add(MergeVaccineGroupForecast.Execute(vaccineGroupName, type, administerFull, contained));
        }

        return results;
    }
}
