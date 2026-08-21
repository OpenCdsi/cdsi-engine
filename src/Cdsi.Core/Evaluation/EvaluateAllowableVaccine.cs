using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>
/// §6.9 Evaluate Allowable Vaccine (Table 6-29). Simpler than Preferable Vaccine — only
/// vaccine type and age window matter, no trade name or volume check.
/// </summary>
public static class EvaluateAllowableVaccine
{
    /// <summary>Evaluates against every allowable-vaccine entry for the target dose; matching any one is sufficient.</summary>
    public static DoseEvaluationOutcome Execute(
        DateOnly dateOfBirth,
        string administeredCvx,
        DateOnly dateAdministered,
        IReadOnlyList<AllowableVaccine> allowableVaccines)
    {
        if (allowableVaccines.Count == 0)
        {
            return DoseEvaluationOutcome.Valid();
        }

        DoseEvaluationOutcome? bestFailure = null;

        foreach (var candidate in allowableVaccines)
        {
            var outcome = EvaluateSingle(dateOfBirth, administeredCvx, dateAdministered, candidate);
            if (outcome.IsValid)
            {
                return outcome;
            }
            bestFailure ??= outcome;
        }

        return bestFailure!;
    }

    private static DoseEvaluationOutcome EvaluateSingle(DateOnly dateOfBirth, string administeredCvx, DateOnly dateAdministered, AllowableVaccine candidate)
    {
        // Rule 2: vaccine type mismatch.
        if (administeredCvx != candidate.Cvx)
        {
            return DoseEvaluationOutcome.NotValid("Not an allowable vaccine for the target dose");
        }

        // Rule 3: age window check.
        var beginDate = candidate.BeginAgeDate(dateOfBirth);
        var endDate = candidate.EndAgeDate(dateOfBirth);
        if (dateAdministered < beginDate || dateAdministered >= endDate)
        {
            return DoseEvaluationOutcome.NotValid(
                "Administered out of the recommended age range for the allowable vaccine");
        }

        // Rule 1: match.
        return DoseEvaluationOutcome.Valid();
    }
}
