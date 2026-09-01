/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Evaluation;
using OpenCdsi.VaxEngine.Core.Models;
using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Core.Pipeline;

/// <summary>
/// §4.4's Figure 4-5 high-level loop: run EvaluateSeriesHistory (§4.4's per-series inner loop)
/// across EVERY relevant patient series for a patient, not just one in isolation. This is the
/// entry point that ties OrganizeImmunizationHistory -> CreateRelevantPatientSeries -> (per
/// series) EvaluateSeriesHistory together into one real "evaluate this patient" call, and its
/// output (specifically each series' CurrentTargetDoseNumber) is exactly what §7 Forecast needs
/// to know what to forecast next.
///
/// KEY DESIGN POINT, directly grounded in §4.4's own text: "An administered dose that is 'valid'
/// for one relevant patient series may be 'not valid' for a different relevant patient series
/// for the same patient." Each series is evaluated completely independently against the SAME
/// raw antigen-administered records - series never share evaluation state with each other, even
/// two series for the same antigen. Only Vaccine Conflict (§6.7) crosses series boundaries, and
/// only for genuinely DIFFERENT antigens (see below).
///
/// SIMPLIFICATION, flagged: when a patient has multiple relevant series for the SAME antigen
/// (a real, documented scenario per §5.1's equivalent series groups), cross-antigen Vaccine
/// Conflict resolution for OTHER antigens' series only sees evaluated-dose history from
/// whichever same-antigen series happened to run first in this pass, not all of them. The
/// underlying administered fact (this CVX was given on this date) is patient-truth regardless
/// of series, but the evaluation STATUS attached to it (which affects CALCDTCONFLICT-2's
/// end-interval branch) can genuinely differ per series - picking one is a reasonable but real
/// simplification, not a spec-mandated resolution.
///
/// §6.2's "Completed Series" condition: `resolveCompletedSeries` takes the ANTIGEN alongside the
/// condition's own `seriesGroups` value, because `seriesGroups` (real data: always "1" or "2")
/// is only meaningful WITHIN one antigen's own file - the same string means something entirely
/// different for a different antigen. This function builds the antigen-scoped closure each
/// individual series' own evaluation actually needs (still a plain `Func&lt;string?, bool&gt;` by
/// the time it reaches EvaluateSeriesHistory/EvaluateConditionalSkip, which don't need to know
/// about antigen-scoping at all) - the caller is responsible for the resolver's actual logic,
/// typically a two-pass approach (see GeneratePatientForecast) since a series in one group often
/// needs to know about ANOTHER group's completion status within the same antigen.
/// </summary>
public static class EvaluatePatientSeriesHistory
{
    /// <param name="assessmentDate">Opt-in, threaded straight through to EvaluateSeriesHistory's own same-named parameter - see its class doc comment. Null (the default) preserves the exact prior behavior.</param>
    public static IReadOnlyDictionary<AntigenSeries, SeriesHistoryResult> Execute(
        Patient patient,
        IReadOnlyList<AntigenSeries> relevantSeries,
        IReadOnlyList<VaccineDoseAdministered> allDosesAdministered,
        IReadOnlyDictionary<string, CvxMapEntry> cvxToAntigen,
        IReadOnlyDictionary<string, IReadOnlyList<VaccineConflictRule>> conflictsByImpactedCvx,
        Func<string, string?, bool> resolveCompletedSeries,
        DateOnly? assessmentDate = null)
    {
        var antigenRecords = OrganizeImmunizationHistory.Execute(patient, allDosesAdministered, cvxToAntigen);

        var results = new Dictionary<AntigenSeries, SeriesHistoryResult>();
        var patientWideHistory = new List<EvaluatedAntigenDose>();

        // Sorted for determinism, not because order is spec-mandated - the spec doesn't say
        // what order relevant series should be evaluated in.
        var orderedSeries = relevantSeries
            .OrderBy(s => s.Antigen, StringComparer.Ordinal)
            .ThenBy(s => s.SeriesName, StringComparer.Ordinal);

        foreach (var series in orderedSeries)
        {
            var thisAntigenRecords = antigenRecords
                .Where(r => r.Antigen == series.Antigen)
                .OrderBy(r => r.DateAdministered)
                .ToArray();

            var otherAntigensHistory = patientWideHistory
                .Where(d => d.Antigen != series.Antigen)
                .ToArray();

            var seriesResult = EvaluateSeriesHistory.Execute(
                patient, series, thisAntigenRecords, otherAntigensHistory,
                conflictsByImpactedCvx, groups => resolveCompletedSeries(series.Antigen, groups), assessmentDate);

            results[series] = seriesResult;

            // Only contribute this antigen's history once - if another relevant series for the
            // SAME antigen runs later, don't let it overwrite/duplicate what's already there
            // (see the SIMPLIFICATION note above).
            if (!patientWideHistory.Any(d => d.Antigen == series.Antigen))
            {
                patientWideHistory.AddRange(seriesResult.AllEvaluatedDoses);
            }
        }

        return results;
    }
}
