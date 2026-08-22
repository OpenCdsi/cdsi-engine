using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

public enum PatientSeriesStatus { NotComplete, Complete, NotRecommended, Immune, Contraindicated, AgedOut }

public sealed class ForecastNeedResult
{
    public required bool ShouldForecast { get; init; }
    public required PatientSeriesStatus PatientSeriesStatus { get; init; }
    public required string Reason { get; init; }
}

/// <summary>
/// §7.4 Determine Forecast Need (Table 7-10). Whether a patient should receive another dose for
/// a relevant patient series - the gate before §7.5 actually generates a forecast date.
///
/// Table 7-10 has 8 rule columns, several of which are independent negative gates (dash/"-"
/// meaning "don't care" for every other condition in that column). Only Column 1 (the positive
/// "should forecast" case) requires ALL of its conditions to hold simultaneously; the rest each
/// fire on a single failing condition regardless of the others. Because more than one negative
/// gate could theoretically be true at once (e.g. a patient could be both aged out AND
/// contraindicated), and the table doesn't state a priority among them, the evaluation order
/// below is an INFERENCE, not spec-grounded: permanent/severe reasons (Contraindicated, Immune,
/// Aged Out) are checked before the temporary one (seasonal), which is checked before the base
/// dose-status logic. Worth a second look if you find spec text that states an explicit order.
///
/// SCOPE NOTE: `candidateEarliestDate` (FORECASTDTCAN-1) is itself computed by §7.5 Generate
/// Forecast Dates, which doesn't exist yet - same forward-dependency shape as Interval/Conflict
/// needing the orchestrator before they could be real. It's nullable here; pass null until §7.5
/// exists, which skips that specific gate rather than defaulting it to a value (e.g. always
/// 12/31/2999) that could silently produce a wrong "Aged Out" result for the common case of an
/// unbounded max age.
/// </summary>
public static class DetermineForecastNeed
{
    public static ForecastNeedResult Execute(
        bool hasNotSatisfiedTargetDose,
        bool hasSatisfiedTargetDose,
        bool hasEvidenceOfImmunity,
        bool isContraindicatedPatientSeries,
        DateOnly assessmentDate,
        SeasonalRecommendation? seasonalRecommendation,
        DateOnly maxAgeDate,
        DateOnly? candidateEarliestDate)
    {
        // Column 5: contraindicated.
        if (isContraindicatedPatientSeries)
        {
            return new ForecastNeedResult { ShouldForecast = false, PatientSeriesStatus = PatientSeriesStatus.Contraindicated, Reason = "Patient has a contraindication" };
        }

        // Column 4: evidence of immunity.
        if (hasEvidenceOfImmunity)
        {
            return new ForecastNeedResult { ShouldForecast = false, PatientSeriesStatus = PatientSeriesStatus.Immune, Reason = "Patient has evidence of immunity" };
        }

        // Column 7: exceeded maximum age.
        if (assessmentDate >= maxAgeDate)
        {
            return new ForecastNeedResult { ShouldForecast = false, PatientSeriesStatus = PatientSeriesStatus.AgedOut, Reason = "Patient has exceeded the maximum age" };
        }

        // Column 8: cannot finish the series before the maximum age (only checked when we have a real candidate earliest date - see scope note).
        if (candidateEarliestDate is DateOnly earliest && earliest >= maxAgeDate)
        {
            return new ForecastNeedResult { ShouldForecast = false, PatientSeriesStatus = PatientSeriesStatus.AgedOut, Reason = "Patient is unable to finish the series prior to the maximum age" };
        }

        // Column 6: past the seasonal recommendation window.
        if (seasonalRecommendation is not null && assessmentDate > seasonalRecommendation.EffectiveEndDate)
        {
            return new ForecastNeedResult { ShouldForecast = false, PatientSeriesStatus = PatientSeriesStatus.NotRecommended, Reason = "Past seasonal recommendation end date" };
        }

        // Column 1: the positive case.
        if (hasNotSatisfiedTargetDose)
        {
            return new ForecastNeedResult { ShouldForecast = true, PatientSeriesStatus = PatientSeriesStatus.NotComplete, Reason = "Patient series is not complete" };
        }

        // Column 2: every target dose satisfied.
        if (hasSatisfiedTargetDose)
        {
            return new ForecastNeedResult { ShouldForecast = false, PatientSeriesStatus = PatientSeriesStatus.Complete, Reason = "Patient series is complete" };
        }

        // Column 3: no target dose is either satisfied or outstanding (e.g. every dose extraneous/skipped).
        return new ForecastNeedResult { ShouldForecast = false, PatientSeriesStatus = PatientSeriesStatus.NotRecommended, Reason = "Not recommended at this time due to past immunization history" };
    }
}
