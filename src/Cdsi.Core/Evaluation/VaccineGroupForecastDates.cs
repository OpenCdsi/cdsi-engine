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
    /// single antigen - discovered running the real, full 30-antigen catalog, not a hypothetical:
    /// a newborn's very first antigen produced two best series with genuinely DIFFERENT statuses
    /// (NotComplete and NotRecommended), not just redundant agreement. SINGLEANTVG-1's own text
    /// ("the patient series status of THE patient series forecast," singular) doesn't address
    /// this combination at all.
    ///
    /// INFERENCE, not spec-grounded, reasoned from what's clinically sound rather than reused
    /// unmodified from Table 9-4: multiple series GROUPS for one antigen are ALTERNATIVE paths
    /// to protecting that antigen, not independent requirements the way multiple ANTIGENS in a
    /// multi-antigen vaccine group are (where "worst status dominates" correctly reflects that
    /// every antigen must be addressed). If ANY alternative path is actively actionable
    /// (NotComplete - a dose is genuinely due via that path), that's the meaningful signal to
    /// report; reporting some other contained status instead would hide a real recommendation
    /// behind a differently-pathed non-recommendation. Only when no contained status is
    /// NotComplete - every path is already resolved or blocked - does this fall back to
    /// MultipleAntigenVaccineGroup's own worst-case cascade, since at that point there's no
    /// actionable path being hidden and a conservative, safety-first default is appropriate.
    /// </summary>
    public static PatientSeriesStatus Status(IReadOnlyList<PatientSeriesStatus> containedStatuses)
    {
        var distinct = containedStatuses.Distinct().ToArray();
        if (distinct.Length == 1)
        {
            return distinct[0];
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
