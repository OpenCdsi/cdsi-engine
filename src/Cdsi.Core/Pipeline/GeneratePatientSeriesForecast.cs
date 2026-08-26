/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Common;
using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Pipeline;

/// <summary>The full §7 forecast for one relevant patient series - the per-series orchestration output this project has been building toward since Chapter 7 began.</summary>
public sealed class PatientSeriesForecastResult
{
    public required PatientSeriesStatus Status { get; init; }
    public required string StatusReason { get; init; }
    public required bool ShouldForecast { get; init; }

    /// <summary>Null when ShouldForecast is false - a Complete/Immune/Contraindicated/etc. series has nothing to forecast dates for.</summary>
    public PatientSeriesForecastDates? Dates { get; init; }

    public IReadOnlyList<string> RecommendedVaccineCvxCodes { get; init; } = Array.Empty<string>();

    /// <summary>Not a named CDSi field - every clinically valid (correct age window, not contraindicated) preferable vaccine for this dose, regardless of CDC's own forecastVaccineType='Y' auto-suggest flag. See DetermineRecommendedVaccine.IsPlausibleSeriesDoseVaccine's doc comment for why this exists: most real doses (~68% of the dataset) have zero flag='Y' entries, so RecommendedVaccineCvxCodes alone is often empty even when valid options exist.</summary>
    public IReadOnlyList<string> AllPreferableVaccineCvxCodes { get; init; } = Array.Empty<string>();

    public int? ForecastDoseNumber { get; init; }
    public IReadOnlyList<string> Guidance { get; init; } = Array.Empty<string>();

    /// <summary>§7.6 Validate Recommendation - null when not applicable (not forecasting).</summary>
    public bool? IsValidRecommendation { get; init; }

    /// <summary>FORECASTPRIORITY-1 - false when ShouldForecast is false (nothing to be a priority forecast about). Consumed by §9.3's MULTIANTVG-1 when merging multi-antigen vaccine group forecasts (MMR, DTaP/Tdap/Td in real data).</summary>
    public bool IsPriorityForecast { get; init; }
}

/// <summary>
/// Wires together nearly all of Chapter 7's sub-pieces (§7.1-§7.6) on top of one series'
/// Chapter 6 evaluation output (SeriesHistoryResult), producing one complete forecast. This is
/// the per-series orchestration layer promised throughout this project's Chapter 7/8/9 work -
/// the Chapter 8 (select best patient series across series groups) and Chapter 9 (merge into
/// vaccine group forecasts) orchestration layers are still separate, larger pieces on top of
/// this one, not included here.
///
/// FORECASTDTCAN-1's `latestConflictEndDate` and `latestInadvertentAdministrationDate`
/// components are now resolved for real (previously caller-supplied placeholders, always null):
/// - `latestInadvertentAdministrationDate` is a plain extraction from `seriesHistory.DoseResults`
///   (already-computed §6.3 evaluation results) - "any target dose in this series whose
///   evaluation reason was 'Inadvertent Administration'," per FORECASTDTCAN-1's own "a target
///   dose that is part of a patient series" (not scoped to just the currently-forecast dose).
/// - `latestConflictEndDate` is CALCDTCONFLICT-3, a genuine new forward-looking calculation
///   (see ForecastConflictEndDate) - reuses the exact same VaccineConflictRule reference data
///   §6.7 already uses, just walked forward instead of backward.
/// </summary>
public static class GeneratePatientSeriesForecast
{
    public static PatientSeriesForecastResult Execute(
        Patient patient,
        AntigenSeries series,
        SeriesHistoryResult seriesHistory,
        DateOnly assessmentDate,
        AntigenImmunityData immunityData,
        AntigenContraindicationData contraindicationData,
        IReadOnlyList<PriorVaccineDoseAdministered> priorDosesAllAntigens,
        IReadOnlyDictionary<string, IReadOnlyList<VaccineConflictRule>> conflictsByImpactedCvx,
        Func<string?, bool> resolveCompletedSeries)
    {
        var hasNotSatisfiedTargetDose = !seriesHistory.SeriesComplete;
        var hasSatisfiedTargetDose = seriesHistory.AllEvaluatedDoses.Any(d => d.SatisfiedTargetDoseNumber is not null);

        // §7.6 Validate Recommendation: "the forecasted dates are beyond the conditional skip
        // requirements of the target dose being forecasted... To prevent erroneous
        // recommendations, this section prospectively ensures the recommendation remains valid
        // at the earliest date. If the recommendation is found to be invalid, re-forecasting for
        // the next target dose is required." Previously, IsValidRecommendation was computed and
        // attached to the result but never actually ACTED on - a real, confirmed gap (found by
        // re-reading this section's own text carefully, not a guess) fixed here: on an invalid
        // forecast, retry against the next target dose in the series, repeating until a valid
        // forecast is found or the series' target doses are exhausted (in which case the spec
        // doesn't say what happens next - returning the last, still-invalid attempt rather than
        // silently picking an earlier one or crashing is a documented, reasonable fallback for
        // that edge case).
        var candidateDoseNumber = seriesHistory.CurrentTargetDoseNumber;
        while (true)
        {
            var currentTargetDose = candidateDoseNumber is int doseNumber
                ? series.SeriesDoses.SingleOrDefault(d => d.DoseNumber == doseNumber)
                : null;

            var attempt = ComputeForecastForTargetDose(
                patient, series, seriesHistory, assessmentDate, immunityData, contraindicationData,
                priorDosesAllAntigens, conflictsByImpactedCvx, resolveCompletedSeries,
                currentTargetDose, hasNotSatisfiedTargetDose, hasSatisfiedTargetDose);

            if (!attempt.ShouldForecast || attempt.IsValidRecommendation != false)
            {
                return attempt;
            }

            var nextDoseNumber = series.SeriesDoses
                .Where(d => d.DoseNumber > currentTargetDose!.DoseNumber)
                .Select(d => (int?)d.DoseNumber)
                .OrderBy(n => n)
                .FirstOrDefault();

            if (nextDoseNumber is null)
            {
                return attempt;
            }

            candidateDoseNumber = nextDoseNumber;
        }
    }

    private static PatientSeriesForecastResult ComputeForecastForTargetDose(
        Patient patient,
        AntigenSeries series,
        SeriesHistoryResult seriesHistory,
        DateOnly assessmentDate,
        AntigenImmunityData immunityData,
        AntigenContraindicationData contraindicationData,
        IReadOnlyList<PriorVaccineDoseAdministered> priorDosesAllAntigens,
        IReadOnlyDictionary<string, IReadOnlyList<VaccineConflictRule>> conflictsByImpactedCvx,
        Func<string?, bool> resolveCompletedSeries,
        SeriesDose? currentTargetDose,
        bool hasNotSatisfiedTargetDose,
        bool hasSatisfiedTargetDose)
    {
        // Contraindication/immunity/age only meaningfully apply when there's a next target dose
        // to evaluate them against - a Complete series (no current target dose) has nothing to
        // check them against, and DetermineForecastNeed's own cascade already resolves such a
        // series to "Complete" via hasSatisfiedTargetDose alone once these are left at their
        // "doesn't apply" defaults (false / unbounded / null).
        var hasEvidenceOfImmunity = currentTargetDose is not null && EvaluateEvidenceOfImmunity.HasEvidenceOfImmunity(patient, immunityData);
        var isContraindicated = currentTargetDose is not null && IsSeriesContraindicated(patient, assessmentDate, currentTargetDose, contraindicationData);

        var maxAgeDate = new DateOnly(2999, 12, 31);
        DateOnly? candidateEarliestDate = null;
        SeasonalRecommendation? seasonalRecommendation = null;

        if (currentTargetDose is not null)
        {
            var applicableAge = currentTargetDose.AgeRules.Count > 0
                ? TemporalRuleSelector.SelectApplicable(currentTargetDose.AgeRules, assessmentDate)
                : null;
            var minAgeDate = applicableAge?.MinAgeDate(patient.DateOfBirth);
            maxAgeDate = applicableAge?.MaxAgeDate(patient.DateOfBirth) ?? maxAgeDate;
            seasonalRecommendation = currentTargetDose.SeasonalRecommendation;

            var resolveIntervalReference = BuildIntervalReferenceResolver(seriesHistory);

            var latestMinIntervalDate = ForecastIntervalDates.LatestMinIntervalDate(
                assessmentDate, currentTargetDose.PreferableIntervals,
                rule => resolveIntervalReference(rule.ReferenceType, rule.ReferenceTargetDoseNumber, rule.ReferenceVaccineCvxCodes));
            var mostRecentAdministeredDate = seriesHistory.AllEvaluatedDoses.Count > 0
                ? seriesHistory.AllEvaluatedDoses.Max(d => d.DateAdministered)
                : (DateOnly?)null;
            var seasonalStart = seasonalRecommendation?.StartDate ?? new DateOnly(1900, 1, 1);

            var latestInadvertentAdministrationDate = seriesHistory.DoseResults
                .Where(r => r.Result.Reason == "Inadvertent Administration")
                .Select(r => (DateOnly?)r.AdministeredDose.DateAdministered)
                .Max();

            var latestConflictEndDate = ForecastConflictEndDate.LatestConflictEndDate(
                currentTargetDose.PreferableVaccines.Select(pv => pv.Cvx).ToArray(),
                priorDosesAllAntigens, conflictsByImpactedCvx);

            candidateEarliestDate = GenerateForecastDates.CalculateCandidateEarliestDate(
                minAgeDate, latestMinIntervalDate, latestConflictEndDate, seasonalStart,
                latestInadvertentAdministrationDate, mostRecentAdministeredDate);
        }

        var forecastNeed = DetermineForecastNeed.Execute(
            hasNotSatisfiedTargetDose, hasSatisfiedTargetDose, hasEvidenceOfImmunity, isContraindicated,
            assessmentDate, seasonalRecommendation, maxAgeDate, candidateEarliestDate);

        if (!forecastNeed.ShouldForecast || currentTargetDose is null)
        {
            return new PatientSeriesForecastResult
            {
                Status = forecastNeed.PatientSeriesStatus,
                StatusReason = forecastNeed.Reason,
                ShouldForecast = false
            };
        }

        var dates = CalculateForecastDates(patient, currentTargetDose, seriesHistory, candidateEarliestDate!.Value, maxAgeDate);

        // Computed once per preferable vaccine ENTRY (not deduplicated by CVX - real data has
        // doses where the same CVX appears more than once with different age windows, e.g.
        // Influenza's own standard series, confirmed by sweeping all 30 files before trusting
        // this), then reused for both the spec-faithful "recommended" (flag='Y' only) list and
        // the additive "plausible" (any clinically valid option) list.
        var vaccinesWithContraindicationStatus = currentTargetDose.PreferableVaccines
            .Select(pv => (Vaccine: pv, IsContraindicated: IsVaccineTypeContraindicated(patient, assessmentDate, pv.Cvx, contraindicationData)))
            .ToArray();

        var recommendedVaccines = vaccinesWithContraindicationStatus
            .Where(x => DetermineRecommendedVaccine.IsRecommendedSeriesDoseVaccine(
                x.Vaccine, x.IsContraindicated, patient.DateOfBirth, dates.EarliestDate, dates.AdjustedRecommendedDate))
            .Select(x => x.Vaccine.Cvx)
            .Distinct()
            .ToArray();

        var allPlausibleVaccines = vaccinesWithContraindicationStatus
            .Where(x => DetermineRecommendedVaccine.IsPlausibleSeriesDoseVaccine(
                x.Vaccine, x.IsContraindicated, patient.DateOfBirth, dates.EarliestDate, dates.AdjustedRecommendedDate))
            .Select(x => x.Vaccine.Cvx)
            .Distinct()
            .ToArray();

        var forecastDoseNumber = DetermineForecastDoseNumber.Execute(
            seriesHistory.AllEvaluatedDoses
                .Where(d => d.SatisfiedTargetDoseNumber is not null)
                .Select(d => new SatisfiedTargetDoseInfo(
                    d.DateAdministered,
                    series.SeriesDoses.SingleOrDefault(sd => sd.DoseNumber == d.SatisfiedTargetDoseNumber!.Value)?.SeasonalRecommendation?.StartDate))
                .ToArray());

        var guidance = GenerateForecastGuidance.Execute(series, patient, contraindicationData.AntigenLevel, contraindicationData.VaccineLevel);

        var priorForSkip = seriesHistory.AllEvaluatedDoses.Select(EvaluateDoseAgainstTargetDose.MapToPriorDoseForSkipOrConflict).ToArray();
        var isValid = ValidateRecommendation.IsValid(
            patient.DateOfBirth, dates.EarliestDate, currentTargetDose.ConditionalSkipInstances, priorForSkip, resolveCompletedSeries);

        // FORECASTPRIORITY-1: one applicable PreferableIntervalRule per reference-point group,
        // resolved the same way ForecastIntervalDates does internally - reused via
        // EvaluatePreferableInterval.GroupByReferencePoint rather than re-implemented, so this
        // can't drift from how §7.5's own interval resolution already works. Anchored to
        // EarliestDate, the same reference point CalculateForecastDates already uses for its own
        // interval lookups.
        var applicableIntervals = EvaluatePreferableInterval.GroupByReferencePoint(currentTargetDose.PreferableIntervals)
            .Select(group => TemporalRuleSelector.SelectApplicable(group, dates.EarliestDate))
            .ToArray();
        var isPriorityForecast = MultipleAntigenVaccineGroup.IsPriorityPatientSeriesForecast(applicableIntervals);

        return new PatientSeriesForecastResult
        {
            Status = forecastNeed.PatientSeriesStatus,
            StatusReason = forecastNeed.Reason,
            ShouldForecast = true,
            Dates = dates,
            RecommendedVaccineCvxCodes = recommendedVaccines,
            AllPreferableVaccineCvxCodes = allPlausibleVaccines,
            ForecastDoseNumber = forecastDoseNumber,
            Guidance = guidance,
            IsValidRecommendation = isValid,
            IsPriorityForecast = isPriorityForecast
        };
    }

    private static bool IsSeriesContraindicated(Patient patient, DateOnly assessmentDate, SeriesDose targetDose, AntigenContraindicationData contraindicationData)
    {
        var anyAntigenContraindicationApplies = contraindicationData.AntigenLevel
            .Any(rule => EvaluateContraindications.EvaluateAntigenContraindication(patient, assessmentDate, rule) == ContraindicationApplicability.Applies);

        var preferableVaccines = targetDose.PreferableVaccines;
        var allPreferableVaccinesContraindicated = preferableVaccines.Count > 0 && preferableVaccines.All(pv =>
            IsVaccineTypeContraindicated(patient, assessmentDate, pv.Cvx, contraindicationData));

        return EvaluateContraindications.IsContraindicatedPatientSeries(anyAntigenContraindicationApplies, allPreferableVaccinesContraindicated);
    }

    private static bool IsVaccineTypeContraindicated(Patient patient, DateOnly assessmentDate, string cvx, AntigenContraindicationData contraindicationData) =>
        contraindicationData.VaccineLevel.Any(rule =>
            EvaluateContraindications.EvaluateVaccineContraindication(patient, assessmentDate, cvx, rule) == ContraindicationApplicability.Applies);

    /// <summary>
    /// Builds the CALCDTINT-1/2/8/9 reference-date resolver shared by both the candidate
    /// earliest date calculation and the recommended-date/past-due-date calculation - the same
    /// interval reference points must resolve consistently across both, so this is built once
    /// per series-forecast call rather than reimplemented (and risking drifting apart) in two
    /// places.
    /// </summary>
    private static Func<IntervalReferenceType, int?, IReadOnlyList<string>, DateOnly?> BuildIntervalReferenceResolver(SeriesHistoryResult seriesHistory)
    {
        var priorThisAntigen = seriesHistory.AllEvaluatedDoses;

        // A recurring target dose (§4.4 step 5/6 - see EvaluateSeriesHistory's own class doc
        // comment) can be satisfied more than once, by design: the SAME target dose number gets
        // satisfied again on each subsequent administered record (annual COVID boosters, Td
        // decade boosters). ToDictionary would throw on that second satisfaction - a genuine,
        // pre-existing crash surfaced by real multi-season COVID-19 conformance cases, not
        // something introduced here. Fixed to keep the LATEST satisfaction date per target dose
        // number, exactly mirroring EvaluateSeriesHistory's own dictionary-building pattern
        // (`targetDoseSatisfiedDates[targetDose.DoseNumber] = adminRecord.DateAdministered`,
        // which naturally overwrites on each new satisfaction rather than throwing).
        var targetDoseSatisfiedDates = new Dictionary<int, DateOnly>();
        foreach (var d in priorThisAntigen.Where(d => d.SatisfiedTargetDoseNumber is not null).OrderBy(d => d.DateAdministered))
        {
            targetDoseSatisfiedDates[d.SatisfiedTargetDoseNumber!.Value] = d.DateAdministered;
        }

        return (type, targetDoseNumber, cvxCodes) => type switch
        {
            IntervalReferenceType.FromPrevious => priorThisAntigen
                .Where(d => d.Status is EvaluationStatus.Valid or EvaluationStatus.NotValid)
                .OrderByDescending(d => d.DateAdministered).FirstOrDefault()?.DateAdministered,
            IntervalReferenceType.FromTargetDose => targetDoseNumber is int tn && targetDoseSatisfiedDates.TryGetValue(tn, out var d) ? d : null,
            IntervalReferenceType.FromMostRecent => priorThisAntigen
                .Where(pd => cvxCodes.Contains(pd.Cvx) && pd.Status != EvaluationStatus.Extraneous)
                .OrderByDescending(pd => pd.DateAdministered).FirstOrDefault()?.DateAdministered,
            IntervalReferenceType.FromRelevantObservation => null,
            _ => null
        };
    }

    private static PatientSeriesForecastDates CalculateForecastDates(
        Patient patient, SeriesDose currentTargetDose, SeriesHistoryResult seriesHistory, DateOnly candidateEarliestDate, DateOnly maxAgeDate)
    {
        var applicableAge = currentTargetDose.AgeRules.Count > 0
            ? TemporalRuleSelector.SelectApplicable(currentTargetDose.AgeRules, candidateEarliestDate)
            : null;

        var resolveIntervalReference = BuildIntervalReferenceResolver(seriesHistory);

        return GenerateForecastDates.Execute(
            candidateEarliestDate,
            earliestRecAgeDate: applicableAge?.EarliestRecAgeDate(patient.DateOfBirth),
            latestEarliestRecIntervalDate: ForecastIntervalDates.LatestEarliestRecIntervalDate(
                candidateEarliestDate, currentTargetDose.PreferableIntervals,
                rule => resolveIntervalReference(rule.ReferenceType, rule.ReferenceTargetDoseNumber, rule.ReferenceVaccineCvxCodes)),
            latestRecAgeDate: applicableAge?.LatestRecAgeDate(patient.DateOfBirth),
            latestLatestRecIntervalDate: ForecastIntervalDates.LatestLatestRecIntervalDate(
                candidateEarliestDate, currentTargetDose.PreferableIntervals,
                rule => resolveIntervalReference(rule.ReferenceType, rule.ReferenceTargetDoseNumber, rule.ReferenceVaccineCvxCodes)),
            maxAgeDate: currentTargetDose.AgeRules.Count > 0 ? maxAgeDate : null);
    }
}
