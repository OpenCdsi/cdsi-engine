using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>
/// §7.5 FORECASTRECVAC-1: is a specific preferable vaccine entry a "recommended series dose
/// vaccine" for a patient series forecast? Requires the forecast-eligible flag, no applicable
/// vaccine contraindication, and the candidate's age window to contain either the forecast's
/// earliest date or its adjusted recommended date.
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
