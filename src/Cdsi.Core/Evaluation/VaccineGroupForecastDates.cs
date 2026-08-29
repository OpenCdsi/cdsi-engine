/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Core.Evaluation;

/// <summary>
/// §9.1 FORECASTVG-2 through 6, FORECASTDN-2: aggregating a vaccine group's forecast dates
/// (and dose number) from the patient series forecasts it contains. Deliberately does NOT
/// compute the vaccine group's own EarliestDate itself - that's §9.2 (single-antigen, trivial)
/// or §9.3 (multi-antigen, needs the not-yet-built "priority patient series forecast" concept)
/// and is passed in here as an already-resolved value.
/// </summary>
public static class VaccineGroupForecastDates
{
    /// <summary>FORECASTVG-2: latest of (earliest AdjustedRecommendedDate among contained forecasts, the vaccine group's own EarliestDate).</summary>
    public static DateOnly AdjustedRecommendedDate(DateOnly vaccineGroupEarliestDate, IReadOnlyList<DateOnly> containedAdjustedRecommendedDates)
    {
        var earliestOfContained = containedAdjustedRecommendedDates.Min();
        return earliestOfContained > vaccineGroupEarliestDate ? earliestOfContained : vaccineGroupEarliestDate;
    }

    /// <summary>FORECASTVG-3: latest of (earliest AdjustedPastDueDate among contained forecasts, the vaccine group's own EarliestDate). Null if no contained forecast has a past due date at all.</summary>
    public static DateOnly? AdjustedPastDueDate(DateOnly vaccineGroupEarliestDate, IReadOnlyList<DateOnly?> containedAdjustedPastDueDates)
    {
        var nonNull = containedAdjustedPastDueDates.Where(d => d is not null).Select(d => d!.Value).ToArray();
        if (nonNull.Length == 0)
        {
            return null;
        }
        var earliestOfContained = nonNull.Min();
        return earliestOfContained > vaccineGroupEarliestDate ? earliestOfContained : vaccineGroupEarliestDate;
    }

    /// <summary>FORECASTVG-4: earliest LatestDate among contained forecasts. Null if none have one.</summary>
    public static DateOnly? LatestDate(IReadOnlyList<DateOnly?> containedLatestDates)
    {
        var nonNull = containedLatestDates.Where(d => d is not null).Select(d => d!.Value).ToArray();
        return nonNull.Length > 0 ? nonNull.Min() : null;
    }

    /// <summary>FORECASTVG-5: earliest UnadjustedRecommendedDate among contained forecasts.</summary>
    public static DateOnly UnadjustedRecommendedDate(IReadOnlyList<DateOnly> containedUnadjustedRecommendedDates) =>
        containedUnadjustedRecommendedDates.Min();

    /// <summary>FORECASTVG-6: earliest UnadjustedPastDueDate among contained forecasts. Null if none have one.</summary>
    public static DateOnly? UnadjustedPastDueDate(IReadOnlyList<DateOnly?> containedUnadjustedPastDueDates)
    {
        var nonNull = containedUnadjustedPastDueDates.Where(d => d is not null).Select(d => d!.Value).ToArray();
        return nonNull.Length > 0 ? nonNull.Min() : null;
    }

    /// <summary>FORECASTDN-2: MIN of contained forecast dose numbers if administerFullVaccineGroup is 'Y', MAX if 'N'. For single-antigen groups (where the flag is typically unset - real data: 24 of 26 groups) there's usually exactly one contained forecast, so MIN==MAX and the choice doesn't matter; the caller can pass either value in that case.</summary>
    public static int ForecastDoseNumber(bool administerFullVaccineGroup, IReadOnlyList<int> containedForecastDoseNumbers) =>
        administerFullVaccineGroup ? containedForecastDoseNumbers.Min() : containedForecastDoseNumbers.Max();
}

/// <summary>§9.2 Single Antigen Vaccine Group (Table 9-3, SINGLEANTVG-1/2).</summary>
public static class SingleAntigenVaccineGroup
{
    /// <summary>
    /// SINGLEANTVG-1: the vaccine group's status is its contained forecast's patient series
    /// status. §8.8 can legitimately produce MORE than one "best patient series" for the same
    /// single antigen. SINGLEANTVG-1's own text ("the patient series status of THE patient
    /// series forecast," singular) doesn't address this combination at all.
    ///
    /// INFERENCE, not spec-grounded. First written to stop a genuine crash (this class's own
    /// earlier design threw on any disagreement) found running Cdsi.Demo's newborn sample
    /// patient against the full 30-antigen catalog, BEFORE the real conformance corpus was even
    /// wired in - resolved by reasoning about which contained status seemed most clinically
    /// meaningful, WITHOUT ever identifying which antigen was involved or verifying the outcome
    /// against a known-correct answer. Worth being explicit about that origin, since it's part of
    /// why the ordering below was revisited and changed once real, verified corpus evidence
    /// became available - the original NotComplete-first preference was reasonable AT THE TIME,
    /// but had never actually been checked against a real expected outcome.
    ///
    /// EXTENDED, then REORDERED, against two real, verified corpus cases:
    /// - 2024-0056 (RSV, 75-year-old, one Arexvy dose): "RSV 1-dose series" comes back AgedOut
    ///   (genuinely not meant for this patient - the dose itself flagged "Inadvertent
    ///   Administration"), "RSV 75 years+ 1-dose series" comes back Complete (this patient
    ///   genuinely finished this real, applicable path). Added Complete as a second preference,
    ///   checked after NotComplete at the time, and confirmed via real execution that this fixed
    ///   the case.
    /// - 2013-0578 (Pneumococcal, one PCV20 dose at 24 months, real corpus says series complete):
    ///   found immediately after, real execution showed this STILL failed - "Pneumococcal start
    ///   at 24 months series" comes back Complete (correct, genuinely applicable to this patient),
    ///   but "Pneumococcal 50+ 1-dose PCV series" ALSO wins §8.8 despite being entirely
    ///   inapplicable (its own dose flagged "Too young"), and its NotComplete status was being
    ///   checked FIRST, hiding the genuine Complete result. The exact same underlying problem as
    ///   AgedOut and NotComplete/NotRecommended above: a contained status from a structurally
    ///   inapplicable series shouldn't override a genuine, resolved signal from an applicable one
    ///   - it just turns out NotComplete can ALSO come from an inapplicable series, not only
    ///   AgedOut, so checking it first was itself the remaining bug.
    ///
    /// Reordered accordingly: Complete is now checked FIRST, before NotComplete - a genuine,
    /// definitive completion via a real applicable path is the strongest signal available, and
    /// checking it first means it can no longer be hidden by a NotComplete (or any other status)
    /// from a differently-pathed, inapplicable series. Only when NEITHER Complete nor NotComplete
    /// appears among the contained statuses - every alternative path is aged out, immune,
    /// contraindicated, or not recommended, with nothing resolved or actionable to report - does
    /// this fall back to MultipleAntigenVaccineGroup's own worst-case cascade.
    /// </summary>
    public static PatientSeriesStatus Status(IReadOnlyList<PatientSeriesStatus> containedStatuses)
    {
        var distinct = containedStatuses.Distinct().ToArray();
        if (distinct.Length == 1)
        {
            return distinct[0];
        }
        if (distinct.Contains(PatientSeriesStatus.Complete))
        {
            return PatientSeriesStatus.Complete;
        }
        if (distinct.Contains(PatientSeriesStatus.NotComplete))
        {
            return PatientSeriesStatus.NotComplete;
        }
        return MultipleAntigenVaccineGroup.Status(containedStatuses);
    }

    /// <summary>SINGLEANTVG-2: the vaccine group's earliest date is the earliest EarliestDate among contained forecasts.</summary>
    public static DateOnly EarliestDate(IReadOnlyList<DateOnly> containedEarliestDates) => containedEarliestDates.Min();
}
