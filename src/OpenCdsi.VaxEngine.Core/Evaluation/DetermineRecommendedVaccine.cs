/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Core.Evaluation;

/// <summary>
/// §7.5 FORECASTRECVAC-1, plus an additive (non-spec-named) companion concept requested by a
/// real user of this engine after noticing most real doses have zero "recommended" vaccines.
///
/// FORECASTRECVAC-1 requires a vaccine's `forecastVaccineType` flag to be 'Y' before it counts
/// as "recommended" - and real CDC data flags only 347 of 1089 total preferableVaccine entries
/// across all 30 antigens that way. The other ~68% are entries CDC's own data intentionally
/// leaves un-flagged for automated default-suggestion (typically combination vaccines, adult-only
/// formulations, or otherwise not the CDC's default auto-pick for that dose) - most real doses
/// legitimately have NO forecastVaccineType='Y' entries at all, which correctly produces an
/// empty recommended-vaccine list. That's faithful behavior, not a gap.
///
/// `IsPlausibleSeriesDoseVaccine` answers a different, useful question: "is this vaccine
/// clinically valid for this dose right now" (correct age window, not contraindicated) WITHOUT
/// requiring CDC's own automated-default flag. `IsRecommendedSeriesDoseVaccine` (the literal
/// FORECASTRECVAC-1 rule) is unchanged in behavior - it now delegates to the shared plausibility
/// check and adds its own flag requirement on top, rather than duplicating the age/contraindication
/// logic in two places that could drift apart.
///
/// "The series dose vaccine is a preferable vaccine" (the rule's first bullet) isn't checked
/// here - it's satisfied by construction, since the caller is evaluating one specific
/// PreferableVaccine entry already drawn from the target dose's own preferable-vaccine list.
///
/// Contraindication status is caller-supplied (from EvaluateContraindications.EvaluateVaccineContraindication
/// against this candidate's CVX) rather than re-derived internally - same pattern as
/// SatisfyTargetDose taking isImpactedByVaccineConflict as a plain bool.
/// </summary>
public static class DetermineRecommendedVaccine
{
    /// <summary>FORECASTRECVAC-1, spec-faithful, behavior unchanged by this refactor.</summary>
    public static bool IsRecommendedSeriesDoseVaccine(
        PreferableVaccine candidate,
        bool isVaccineTypeContraindicated,
        DateOnly dateOfBirth,
        DateOnly earliestDate,
        DateOnly adjustedRecommendedDate)
    {
        if (!candidate.ForecastVaccineTypeFlag)
        {
            return false;
        }
        return IsPlausibleSeriesDoseVaccine(candidate, isVaccineTypeContraindicated, dateOfBirth, earliestDate, adjustedRecommendedDate);
    }

    /// <summary>Not a named CDSi rule - the same age-window/contraindication check FORECASTRECVAC-1 uses, without requiring the forecastVaccineType='Y' flag. "Everything clinically valid for this dose," not "everything CDC flags as an automated default."</summary>
    public static bool IsPlausibleSeriesDoseVaccine(
        PreferableVaccine candidate,
        bool isVaccineTypeContraindicated,
        DateOnly dateOfBirth,
        DateOnly earliestDate,
        DateOnly adjustedRecommendedDate)
    {
        if (isVaccineTypeContraindicated)
        {
            return false;
        }

        var beginDate = candidate.BeginAgeDate(dateOfBirth);
        var endDate = candidate.EndAgeDate(dateOfBirth);

        var earliestDateInWindow = earliestDate >= beginDate && earliestDate < endDate;
        var recommendedDateInWindow = adjustedRecommendedDate >= beginDate && adjustedRecommendedDate < endDate;

        return earliestDateInWindow || recommendedDateInWindow;
    }
}
