/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Models;
using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>
/// Runs the complete §6.1-6.10 pipeline for one administered-dose-vs-target-dose pairing.
///
/// CORRECTION to the mental model established earlier in this project's design conversation:
/// §6.1, §6.2, and §6.3 are NOT peer conditions that feed §6.10's aggregator - each produces a
/// complete target-dose outcome on its own and short-circuits everything after it (Table 6-3's
/// "Sub-standard", Table 6-11's "Skipped", and §6.3's own "Not Valid / Inadvertent
/// Administration" outcome are all final results, not booleans Table 6-31 combines). Only
/// §6.4-6.9 (Age, Interval x2, Vaccine Conflict, Vaccine x2) are the true AND-able conditions
/// §6.10 aggregates. This only became clear while wiring the components together - re-reading
/// Table 6-31's exact condition list confirmed Inadvertent Vaccine was never one of its inputs.
///
/// SCOPE: "Completed Series" (§6.2's Table 6-7) remains caller-supplied. §6.5's FromMostRecent/
/// FromRelevantObservation reference-date resolution is simplified (FromMostRecent excludes
/// Extraneous prior doses; FromRelevantObservation always resolves to null/not-applicable,
/// since no patient-observation-date tracking exists yet in this codebase).
/// </summary>
public static class EvaluateDoseAgainstTargetDose
{
    /// <param name="priorDosesOfThisAntigen">This antigen's own prior EVALUATED doses, chronologically before the current one - used for Interval and Conditional Skip, both of which are properly series-scoped per their business rules (CALCDTINT-1's "immediate previous vaccine dose administered" means within this series).</param>
    /// <param name="priorDosesAllAntigens">The patient's FULL prior evaluated dose history across every antigen - used only for Vaccine Conflict (§6.7), since conflicting pairs are frequently cross-antigen (e.g. MMR vs Varicella).</param>
    /// <param name="targetDoseSatisfiedDates">target dose number -> date administered that satisfied it, for this series so far - needed for Interval's FromTargetDose reference resolution.</param>
    /// <param name="resolveCompletedSeries">See EvaluateConditionalSkip's scope note - not resolvable internally yet.</param>
    public static TargetDoseEvaluationResult Execute(
        Patient patient,
        VaccineDoseAdministered administeredDose,
        SeriesDose targetDose,
        IReadOnlyList<EvaluatedAntigenDose> priorDosesOfThisAntigen,
        IReadOnlyList<EvaluatedAntigenDose> priorDosesAllAntigens,
        IReadOnlyDictionary<int, DateOnly> targetDoseSatisfiedDates,
        IReadOnlyDictionary<string, IReadOnlyList<VaccineConflictRule>> conflictsByImpactedCvx,
        Func<string?, bool> resolveCompletedSeries)
    {
        // §6.1 - gate.
        var doseCondition = EvaluateDoseAdministeredCondition.Execute(
            administeredDose.DateAdministered, administeredDose.LotExpirationDate, administeredDose.DoseConditionFlag);
        if (!doseCondition.CanBeEvaluated)
        {
            return TargetDoseEvaluationResult.NotSatisfied(EvaluationStatus.SubStandard, doseCondition.Reason!);
        }

        // §6.2 - gate.
        var priorForSkip = priorDosesOfThisAntigen.Select(MapToPriorDoseForSkipOrConflict).ToArray();
        var canSkip = EvaluateConditionalSkip.CanBeSkipped(
            patient.DateOfBirth, administeredDose.DateAdministered, ConditionalSkipContext.Evaluation, targetDose.ConditionalSkipInstances, priorForSkip, resolveCompletedSeries);
        if (canSkip)
        {
            return TargetDoseEvaluationResult.Skipped();
        }

        // §6.3 - gate.
        var inadvertent = EvaluateInadvertentVaccine.Execute(administeredDose.Cvx, targetDose.InadvertentVaccineCvxCodes);
        if (!inadvertent.IsValid)
        {
            return TargetDoseEvaluationResult.NotSatisfied(EvaluationStatus.NotValid, inadvertent.Reason!);
        }

        // §6.4-6.9 - the true AND-able conditions §6.10 aggregates.
        var age = EvaluateAge.Execute(patient.DateOfBirth, administeredDose.DateAdministered, targetDose.AgeRules);

        var mostImmediatePrevious = priorDosesOfThisAntigen
            .Where(d => d.Status is EvaluationStatus.Valid or EvaluationStatus.NotValid) // CALCDTINT-1: only Valid/Not Valid doses are eligible to be "the previous dose"
            .OrderByDescending(d => d.DateAdministered)
            .FirstOrDefault();

        DateOnly? ResolveIntervalReference(IntervalReferenceType type, int? targetDoseNumber, IReadOnlyList<string> cvxCodes)
        {
            return type switch
            {
                IntervalReferenceType.FromPrevious => mostImmediatePrevious?.DateAdministered,
                IntervalReferenceType.FromTargetDose => targetDoseNumber is int n && targetDoseSatisfiedDates.TryGetValue(n, out var d) ? d : null,
                IntervalReferenceType.FromMostRecent => priorDosesOfThisAntigen
                    .Where(pd => cvxCodes.Contains(pd.Cvx) && pd.Status != EvaluationStatus.Extraneous)
                    .OrderByDescending(pd => pd.DateAdministered)
                    .FirstOrDefault()?.DateAdministered,
                IntervalReferenceType.FromRelevantObservation => null, // not modeled - see class doc comment
                _ => null
            };
        }

        var preferableInterval = EvaluatePreferableInterval.Execute(
            administeredDose.DateAdministered, targetDose.PreferableIntervals,
            rule => ResolveIntervalReference(rule.ReferenceType, rule.ReferenceTargetDoseNumber, rule.ReferenceVaccineCvxCodes));

        var allowableInterval = EvaluateAllowableInterval.Execute(
            administeredDose.DateAdministered, targetDose.AllowableIntervals,
            rule => ResolveIntervalReference(rule.ReferenceType, rule.ReferenceTargetDoseNumber, Array.Empty<string>()));

        var priorForConflict = priorDosesAllAntigens.Select(MapToPriorDoseForSkipOrConflict).ToArray();
        var conflict = EvaluateVaccineConflict.Execute(
            administeredDose.Cvx, administeredDose.DateAdministered, priorForConflict, conflictsByImpactedCvx);

        var preferableVaccine = EvaluatePreferableVaccine.Execute(patient.DateOfBirth, administeredDose, targetDose.PreferableVaccines);
        var allowableVaccine = EvaluateAllowableVaccine.Execute(
            patient.DateOfBirth, administeredDose.Cvx, administeredDose.DateAdministered, targetDose.AllowableVaccines);

        // §6.10 aggregator.
        var aggregate = SatisfyTargetDose.Execute(
            age, preferableInterval, allowableInterval,
            isImpactedByVaccineConflict: !conflict.IsValid,
            isPreferableOrAllowableVaccine: preferableVaccine.IsValid || allowableVaccine.IsValid);

        if (aggregate.IsExtraneous)
        {
            return TargetDoseEvaluationResult.NotSatisfied(EvaluationStatus.Extraneous, aggregate.Reason ?? "Extraneous");
        }
        if (!aggregate.IsValid)
        {
            return TargetDoseEvaluationResult.NotSatisfied(EvaluationStatus.NotValid, aggregate.Reason ?? "Not valid");
        }
        return TargetDoseEvaluationResult.Satisfied(aggregate.Reason);
    }

    internal static PriorVaccineDoseAdministered MapToPriorDoseForSkipOrConflict(EvaluatedAntigenDose dose) => new(
        dose.Cvx,
        dose.DateAdministered,
        dose.Status switch
        {
            EvaluationStatus.Valid => PriorDoseEvaluationStatus.Valid,
            EvaluationStatus.NotValid or EvaluationStatus.Extraneous or EvaluationStatus.SubStandard => PriorDoseEvaluationStatus.NotValid,
            null => null,
            _ => null
        });
}
