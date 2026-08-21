namespace Cdsi.Core.Evaluation;

/// <summary>
/// §6.10 Satisfy Target Dose (Table 6-31) — the aggregator that combines the independent
/// §6.1-6.9 logical-component outcomes into one final result per target dose.
///
/// Table 6-31 has two parallel output columns per rule: "Target Dose Status"
/// (Satisfied/Not Satisfied) and "Evaluation Status" (Valid/Extraneous/Not Valid). These
/// collapse onto the existing <see cref="DoseEvaluationOutcome"/> shape exactly:
/// IsValid=true ⇔ Satisfied+Valid; IsValid=false,IsExtraneous=true ⇔ Not Satisfied+Extraneous;
/// IsValid=false,IsExtraneous=false ⇔ Not Satisfied+Not Valid. No new type needed.
///
/// SCOPE NOTE: §6.7 Vaccine Conflict and §6.8/6.9 Preferable/Allowable Vaccine haven't been
/// built yet, so their outcomes are taken as plain booleans here rather than computed
/// internally — same pattern as the Interval reference-date resolver. Once those components
/// exist, the caller should pass their real outcomes through instead of hand-computing them.
/// </summary>
public static class SatisfyTargetDose
{
    /// <param name="ageOutcome">§6.4 result.</param>
    /// <param name="preferableIntervalOutcome">§6.5 result.</param>
    /// <param name="allowableIntervalOutcome">§6.6 result.</param>
    /// <param name="isImpactedByVaccineConflict">§6.7 result — not yet built; caller-supplied for now.</param>
    /// <param name="isPreferableOrAllowableVaccine">§6.8/6.9 result — not yet built; caller-supplied for now.</param>
    public static DoseEvaluationOutcome Execute(
        DoseEvaluationOutcome ageOutcome,
        DoseEvaluationOutcome preferableIntervalOutcome,
        DoseEvaluationOutcome allowableIntervalOutcome,
        bool isImpactedByVaccineConflict,
        bool isPreferableOrAllowableVaccine)
    {
        // Rule 2: Age Extraneous short-circuits everything else.
        if (ageOutcome.IsExtraneous)
        {
            return DoseEvaluationOutcome.NotValid(ageOutcome.Reason ?? "Too old", isExtraneous: true);
        }

        // Rule 3: Age not valid (and not extraneous).
        if (!ageOutcome.IsValid)
        {
            return DoseEvaluationOutcome.NotValid(ageOutcome.Reason ?? "Age not valid");
        }

        // Rule 4: Table 6-31's exact wording is "satisfy ALL preferable intervals OR ALL
        // allowable intervals" - an OR between the two Interval components we built, not a
        // choice of only one.
        var intervalSatisfied = preferableIntervalOutcome.IsValid || allowableIntervalOutcome.IsValid;
        if (!intervalSatisfied)
        {
            var reason = preferableIntervalOutcome.Reason ?? allowableIntervalOutcome.Reason ?? "Interval not satisfied";
            return DoseEvaluationOutcome.NotValid(reason);
        }

        // Rule 5: impacted by a vaccine conflict.
        if (isImpactedByVaccineConflict)
        {
            return DoseEvaluationOutcome.NotValid("Impacted by vaccine conflict");
        }

        // Rule 6: not a preferable or allowable vaccine for the target dose.
        if (!isPreferableOrAllowableVaccine)
        {
            return DoseEvaluationOutcome.NotValid("Not a preferable or allowable vaccine for the target dose");
        }

        // Rule 1: everything passed.
        return DoseEvaluationOutcome.Valid();
    }
}
