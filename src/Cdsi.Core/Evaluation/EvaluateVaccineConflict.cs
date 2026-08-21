using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>Evaluation status of a prior administered dose, as needed by CALCDTCONFLICT-2. This is a strict subset of the full Chapter 6 outcome space — only Valid vs Not Valid vs unknown/not-yet-evaluated matter here.</summary>
public enum PriorDoseEvaluationStatus { Valid, NotValid }

/// <summary>A previously administered dose, as needed to resolve vaccine conflict business rules. EvaluationStatus is nullable to represent CALCDTCONFLICT-2's explicit "no evaluation status" case (a dose that hasn't been run through §6.10 yet) — which the spec treats the same as 'Valid', not the same as 'Not Valid'.</summary>
public sealed record PriorVaccineDoseAdministered(string Cvx, DateOnly DateAdministered, PriorDoseEvaluationStatus? EvaluationStatus);

/// <summary>
/// §6.7 Evaluate Vaccine Conflict (CALCDTCONFLICT-1/2, CONFLICT-3). Unlike Interval, this
/// component's reference-date resolution doesn't require knowing "which dose satisfied target
/// dose N" — only each prior dose's CVX, date, and evaluation status — so it's fully wired up
/// here rather than deferred behind a resolver placeholder like Interval was.
/// </summary>
public static class EvaluateVaccineConflict
{
    public static DoseEvaluationOutcome Execute(
        string currentCvx,
        DateOnly currentDateAdministered,
        IReadOnlyList<PriorVaccineDoseAdministered> priorDoses,
        IReadOnlyDictionary<string, IReadOnlyList<VaccineConflictRule>> conflictsByImpactedCvx)
    {
        // §6.7: "if no vaccine Supporting Data exists for the vaccine type of the vaccine dose
        // administered being evaluated, the vaccine dose administered is not in conflict with
        // any other vaccine dose administered."
        if (!conflictsByImpactedCvx.TryGetValue(currentCvx, out var applicableRules) || applicableRules.Count == 0)
        {
            return DoseEvaluationOutcome.Valid();
        }

        foreach (var prior in priorDoses)
        {
            var rule = applicableRules.FirstOrDefault(r => r.ConflictingCvx == prior.Cvx);
            if (rule is null)
            {
                continue; // this prior dose's vaccine type doesn't conflict with the current one
            }

            // CALCDTCONFLICT-1
            var conflictBeginDate = rule.ConflictBeginInterval.AddTo(prior.DateAdministered);

            // CALCDTCONFLICT-2: minimum end interval if the prior dose is Valid OR has no
            // evaluation status yet; the (longer) full end interval only when the prior dose
            // has an evaluation status AND it is specifically NOT 'Valid'.
            var usesMinimumEndInterval = prior.EvaluationStatus is null or PriorDoseEvaluationStatus.Valid;
            var conflictEndDate = usesMinimumEndInterval
                ? rule.MinConflictEndInterval.AddTo(prior.DateAdministered)
                : rule.ConflictEndInterval.AddTo(prior.DateAdministered);

            // CONFLICT-3
            if (currentDateAdministered >= conflictBeginDate && currentDateAdministered < conflictEndDate)
            {
                return DoseEvaluationOutcome.NotValid(
                    $"Conflicts with {prior.Cvx} administered {prior.DateAdministered:yyyy-MM-dd}");
            }
        }

        return DoseEvaluationOutcome.Valid();
    }
}
