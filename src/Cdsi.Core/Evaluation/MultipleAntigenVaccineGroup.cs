/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>§9.3 Multiple Antigen Vaccine Group (Table 9-4, MULTIANTVG-1, FORECASTPRIORITY-1).</summary>
public static class MultipleAntigenVaccineGroup
{
    /// <summary>
    /// Table 9-4: the vaccine group's status is a strict priority cascade over the statuses of
    /// its contained patient series forecasts - the first matching condition wins, checked in
    /// this exact order. By the time the cascade reaches the final fallback, every status other
    /// than Complete/Immune has already been ruled out and "not all Immune" has been confirmed,
    /// so "all remaining are Complete or Immune" is automatically true - no separate check needed.
    /// </summary>
    public static PatientSeriesStatus Status(IReadOnlyList<PatientSeriesStatus> containedStatuses)
    {
        if (containedStatuses.Any(s => s == PatientSeriesStatus.Contraindicated)) return PatientSeriesStatus.Contraindicated;
        if (containedStatuses.Any(s => s == PatientSeriesStatus.AgedOut)) return PatientSeriesStatus.AgedOut;
        if (containedStatuses.Any(s => s == PatientSeriesStatus.NotRecommended)) return PatientSeriesStatus.NotRecommended;
        if (containedStatuses.Any(s => s == PatientSeriesStatus.NotComplete)) return PatientSeriesStatus.NotComplete;
        if (containedStatuses.All(s => s == PatientSeriesStatus.Immune)) return PatientSeriesStatus.Immune;
        return PatientSeriesStatus.Complete;
    }

    /// <summary>
    /// FORECASTPRIORITY-1: a patient series forecast is a "priority" forecast if the forecasted
    /// target dose has at least one preferable interval AND every one of them has the interval
    /// priority flag set. Takes the already-temporally-resolved set of applicable preferable
    /// interval rules for the forecasted target dose (one per reference-point group) - not the
    /// raw unresolved list - consistent with how every other Interval-consuming function in this
    /// codebase takes resolved input rather than re-deriving resolution itself.
    /// </summary>
    public static bool IsPriorityPatientSeriesForecast(IReadOnlyList<PreferableIntervalRule> applicablePreferableIntervals)
    {
        if (applicablePreferableIntervals.Count == 0)
        {
            return false;
        }
        return applicablePreferableIntervals.All(interval => interval.IsPriorityOverride);
    }

    /// <summary>
    /// MULTIANTVG-1: the vaccine group's earliest date. If any contained forecast is a priority
    /// forecast, it's the LATER of (the earliest EarliestDate among contained forecasts, the
    /// latest date administered among the patient's doses of any vaccine type belonging to the
    /// group). Otherwise it's simply the LATEST EarliestDate among contained forecasts.
    /// </summary>
    public static DateOnly EarliestDate(
        bool anyContainedForecastIsPriority,
        IReadOnlyList<DateOnly> containedEarliestDates,
        DateOnly? latestAdministeredDateOfGroupVaccineTypes)
    {
        if (!anyContainedForecastIsPriority)
        {
            return containedEarliestDates.Max();
        }

        var earliestOfContained = containedEarliestDates.Min();
        if (latestAdministeredDateOfGroupVaccineTypes is DateOnly lastAdministered && lastAdministered > earliestOfContained)
        {
            return lastAdministered;
        }
        return earliestOfContained;
    }
}
