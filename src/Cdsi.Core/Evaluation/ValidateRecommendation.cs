using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>
/// §7.6 Validate Recommendation / §7.6.1 Conditional Skip. Re-runs Conditional Skip (the same
/// §6.2/§7.1 machinery, Forecast context) but with the reference date set to the forecast's own
/// EarliestDate (FORECASTDT-1) rather than the assessment date used for the initial §7.1 check -
/// the spec's own instruction: "In CONDSKIP-2, the Earliest Date is used."
///
/// The intent (per the surrounding prose): a forecast can become stale by the time its earliest
/// date actually arrives - e.g. a patient behind on Hib gets a catch-up dose recommended in 4
/// weeks, but by the time 4 weeks pass, updated conditional-skip logic based on the patient's
/// now-older age would have skipped that dose entirely and recommended something further out
/// instead. If Conditional Skip says "yes, skippable" using the forecast's own earliest date as
/// the reference point, the forecast is invalid and the caller should re-forecast (for the next
/// target dose) rather than present this one.
///
/// This is a thin, deliberately small wrapper - all the real logic already exists in
/// EvaluateConditionalSkip. §7.6 doesn't introduce a new decision table of its own.
/// </summary>
public static class ValidateRecommendation
{
    public static bool IsValid(
        DateOnly dateOfBirth,
        DateOnly forecastEarliestDate,
        IReadOnlyList<ConditionalSkipInstance> conditionalSkipInstances,
        IReadOnlyList<PriorVaccineDoseAdministered> priorDosesOfThisAntigen,
        Func<string?, bool> resolveCompletedSeries)
    {
        var canBeSkippedAtEarliestDate = EvaluateConditionalSkip.CanBeSkipped(
            dateOfBirth, forecastEarliestDate, ConditionalSkipContext.Forecast,
            conditionalSkipInstances, priorDosesOfThisAntigen, resolveCompletedSeries);

        return !canBeSkippedAtEarliestDate;
    }
}
