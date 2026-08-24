/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Core.Evaluation;

/// <summary>
/// SELECTB-6 / SELECTB-16 / SELECTB-21 - shared classification used by both §8.2 (Identify One
/// Prioritized Patient Series) and §8.3 (Classify Scorable Patient Series). Kept as one small
/// module since both sections use the identical definitions rather than duplicating them.
/// Deliberately takes plain statuses rather than Pipeline's richer DoseEvaluationRecord type -
/// Evaluation shouldn't reach back into Pipeline, which is built on top of it.
/// </summary>
public static class ClassifyScorablePatientSeries
{
    /// <summary>SELECTB-6: complete if the patient series forecast (§7.4's DetermineForecastNeed result) has status 'Complete'.</summary>
    public static bool IsCompletePatientSeries(PatientSeriesStatus status) => status == PatientSeriesStatus.Complete;

    /// <summary>SELECTB-16: in-process if at least one target dose is Satisfied AND the forecast status is 'Not Complete'.</summary>
    public static bool IsInProcessPatientSeries(bool hasAtLeastOneSatisfiedTargetDose, PatientSeriesStatus status) =>
        hasAtLeastOneSatisfiedTargetDose && status == PatientSeriesStatus.NotComplete;

    /// <summary>SELECTB-21: the count of target doses with target dose status 'Satisfied' - "number of valid doses" in Chapter 8's terminology.</summary>
    public static int CountValidDoses(IReadOnlyList<TargetDoseStatus> targetDoseStatuses) =>
        targetDoseStatuses.Count(s => s == TargetDoseStatus.Satisfied);

    /// <summary>
    /// §8.3 Table 8-5: which subset of scorable patient series in a group should actually be
    /// scored. By the time a series group reaches §8.3 at all, §8.2 has already ruled out a
    /// complete/in-process count of exactly 1 (either would have already won outright as the
    /// single prioritized series) - so the counts reaching here are always 0 or 2+ for each,
    /// which is what makes Table 8-5's three columns collectively exhaustive for the cases that
    /// actually arrive.
    /// </summary>
    public static ScoringCategory DetermineScoringCategory(int completeCount, int inProcessCount, bool allScorableSeriesHaveZeroValidDoses)
    {
        if (completeCount >= 2)
        {
            return ScoringCategory.CompletePatientSeries;
        }
        if (completeCount == 0 && inProcessCount >= 2)
        {
            return ScoringCategory.InProcessPatientSeries;
        }
        if (allScorableSeriesHaveZeroValidDoses)
        {
            return ScoringCategory.NoValidDoses;
        }
        // Not spec-covered: reachable only if a series has some valid doses but qualifies for
        // neither Complete nor In-Process (e.g. a forecast status other than Complete/NotComplete,
        // such as Contraindicated, on a series with 1+ satisfied dose). Table 8-5 doesn't name an
        // explicit outcome for this combination - flagged rather than guessed at.
        return ScoringCategory.Undetermined;
    }
}

/// <summary>Which point-scoring rules (§8.4/§8.5/§8.6) apply to a series group's scorable series, per Table 8-5.</summary>
public enum ScoringCategory { CompletePatientSeries, InProcessPatientSeries, NoValidDoses, Undetermined }
