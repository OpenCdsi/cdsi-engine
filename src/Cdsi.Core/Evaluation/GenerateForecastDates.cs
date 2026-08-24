/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Core.Evaluation;

/// <summary>The six calculated dates that make up a "patient series forecast" (§7.5, Tables 7-12/7-13).</summary>
public sealed class PatientSeriesForecastDates
{
    /// <summary>FORECASTDT-1: candidate earliest date (FORECASTDTCAN-1) - always well-defined, since the seasonal recommendation start date component always has a real value (defaults to 1900-01-01).</summary>
    public required DateOnly EarliestDate { get; init; }

    /// <summary>FORECASTDT-2: always well-defined - the tiered fallback's final tier is EarliestDate itself.</summary>
    public required DateOnly UnadjustedRecommendedDate { get; init; }

    /// <summary>FORECASTDT-3: null ("blank") if there's no latest recommended age date AND no latest recommended interval date at all.</summary>
    public DateOnly? UnadjustedPastDueDate { get; init; }

    /// <summary>FORECASTDT-4: null ("blank") if there's no maximum age date for this target dose (i.e. no AgeRule exists at all - NOT the same as an AgeRule existing with an empty MaxAge sub-field, which per §6.4's own default already resolves to 2999-12-31 upstream).</summary>
    public DateOnly? LatestDate { get; init; }

    /// <summary>FORECASTDT-5: always well-defined - MAX(EarliestDate, UnadjustedRecommendedDate).</summary>
    public required DateOnly AdjustedRecommendedDate { get; init; }

    /// <summary>FORECASTDT-6: null ("blank") only when UnadjustedPastDueDate is null.</summary>
    public DateOnly? AdjustedPastDueDate { get; init; }
}

/// <summary>
/// §7.5 Generate Forecast Dates (Tables 7-12/7-13) - the six-date calculation core. Recommended
/// vaccine selection (FORECASTRECVAC-1), administrative guidance text (FORECASTGUIDANCE-1), and
/// forecast dose number (FORECASTDN-1) are deliberately out of scope for this round - each is
/// its own separate concern with its own real complexity, not folded in here.
///
/// Table 7-12's own instruction is explicit and different from almost everywhere else in this
/// codebase: "If an attribute value is empty, then the date calculations will remain empty. No
/// assumptions will be made for the attribute." Unlike Chapter 6's pervasive 1900/2999 sentinel
/// defaults, several of these inputs are genuinely nullable and must stay that way through the
/// calculation - hence every parameter below being DateOnly? rather than silently defaulted.
/// </summary>
public static class GenerateForecastDates
{
    /// <summary>FORECASTDTCAN-1: MAX of the six listed date components, skipping any that are null. seasonalRecommendationStartDate is the one component with a real default (1900-01-01, per Table 7-12) - the caller should already have applied that before calling, which is why this parameter isn't itself nullable.</summary>
    public static DateOnly CalculateCandidateEarliestDate(
        DateOnly? minAgeDate,
        DateOnly? latestMinIntervalDate,
        DateOnly? latestConflictEndDate,
        DateOnly seasonalRecommendationStartDate,
        DateOnly? latestInadvertentAdministrationDate,
        DateOnly? mostRecentAdministeredDate)
    {
        DateOnly result = seasonalRecommendationStartDate;
        foreach (var candidate in new[] { minAgeDate, latestMinIntervalDate, latestConflictEndDate, latestInadvertentAdministrationDate, mostRecentAdministeredDate })
        {
            if (candidate is DateOnly d && d > result)
            {
                result = d;
            }
        }
        return result;
    }

    public static PatientSeriesForecastDates Execute(
        DateOnly candidateEarliestDate,
        DateOnly? earliestRecAgeDate,
        DateOnly? latestEarliestRecIntervalDate,
        DateOnly? latestRecAgeDate,
        DateOnly? latestLatestRecIntervalDate,
        DateOnly? maxAgeDate)
    {
        var earliestDate = candidateEarliestDate; // FORECASTDT-1

        // FORECASTDT-2: tiered fallback, always resolves.
        var unadjustedRecommendedDate = earliestRecAgeDate ?? latestEarliestRecIntervalDate ?? earliestDate;

        // FORECASTDT-3: tiered fallback, can be blank.
        DateOnly? unadjustedPastDueDate = latestRecAgeDate is DateOnly lrad
            ? lrad.AddDays(-1)
            : latestLatestRecIntervalDate is DateOnly lrid
                ? lrid.AddDays(-1)
                : null;

        // FORECASTDT-4.
        DateOnly? latestDate = maxAgeDate?.AddDays(-1);

        // FORECASTDT-5: MAX(earliestDate, unadjustedRecommendedDate).
        var adjustedRecommendedDate = unadjustedRecommendedDate > earliestDate ? unadjustedRecommendedDate : earliestDate;

        // FORECASTDT-6: MAX(earliestDate, unadjustedPastDueDate) if unadjustedPastDueDate exists, else blank.
        DateOnly? adjustedPastDueDate = unadjustedPastDueDate is DateOnly upd
            ? (upd > earliestDate ? upd : earliestDate)
            : null;

        return new PatientSeriesForecastDates
        {
            EarliestDate = earliestDate,
            UnadjustedRecommendedDate = unadjustedRecommendedDate,
            UnadjustedPastDueDate = unadjustedPastDueDate,
            LatestDate = latestDate,
            AdjustedRecommendedDate = adjustedRecommendedDate,
            AdjustedPastDueDate = adjustedPastDueDate
        };
    }
}
