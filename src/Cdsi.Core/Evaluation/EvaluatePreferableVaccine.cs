/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>
/// §6.8 Evaluate Preferable Vaccine (Table 6-26). Checks whether the administered vaccine
/// matches one of the target dose's preferable-vaccine entries.
///
/// IMPORTANT — not a simple AND of all conditions: per Table 6-26's exact rule ordering,
/// insufficient volume does NOT flip the outcome to "not preferable." It's still "Yes, a
/// preferable vaccine," just with an informational reason attached ("Volume administered is
/// less than recommended volume"). Only a vaccine-type mismatch, an out-of-range age, or a
/// trade-name mismatch make it "No." Getting this ordering backwards (treating volume as a
/// hard gate) is an easy mistake the real table specifically doesn't make.
/// </summary>
public static class EvaluatePreferableVaccine
{
    /// <summary>Evaluates against every preferable-vaccine entry for the target dose; the dose only needs to match ONE to be "a preferable vaccine" (this mirrors how the table describes matching "a preferable vaccine for the target dose," singular match, not all of them).</summary>
    public static DoseEvaluationOutcome Execute(
        DateOnly dateOfBirth,
        Models.VaccineDoseAdministered dose,
        IReadOnlyList<PreferableVaccine> preferableVaccines)
    {
        if (preferableVaccines.Count == 0)
        {
            // No preferable vaccine constraint defined for this target dose - nothing to fail.
            return DoseEvaluationOutcome.Valid();
        }

        DoseEvaluationOutcome? bestFailure = null;

        foreach (var candidate in preferableVaccines)
        {
            var outcome = EvaluateSingle(dateOfBirth, dose, candidate);
            if (outcome.IsValid)
            {
                return outcome; // first match wins
            }
            bestFailure ??= outcome;
        }

        return bestFailure!;
    }

    private static DoseEvaluationOutcome EvaluateSingle(DateOnly dateOfBirth, Models.VaccineDoseAdministered dose, PreferableVaccine candidate)
    {
        // Rule 3: vaccine type mismatch - immediate No.
        if (dose.Cvx != candidate.Cvx)
        {
            return DoseEvaluationOutcome.NotValid("Not a preferable vaccine for the target dose");
        }

        // Rule 4: age window check.
        var beginDate = candidate.BeginAgeDate(dateOfBirth);
        var endDate = candidate.EndAgeDate(dateOfBirth);
        if (dose.DateAdministered < beginDate || dose.DateAdministered >= endDate)
        {
            return DoseEvaluationOutcome.NotValid(
                "Administered out of the recommended age range for the preferable vaccine");
        }

        // Rule 5: trade name check (only applies if the target dose specifies one - absent for
        // the large majority of real preferableVaccine entries).
        if (candidate.TradeName is not null && dose.TradeName != candidate.TradeName)
        {
            return DoseEvaluationOutcome.NotValid(
                "Trade name of the vaccine dose administered is not the same as the trade name of the preferable vaccine");
        }

        // Rule 2: volume check - does NOT flip the outcome, only adds a reason.
        if (candidate.Volume is double requiredVolume && dose.Volume is double actualVolume && actualVolume < requiredVolume)
        {
            return DoseEvaluationOutcome.Valid("Volume administered is less than recommended volume");
        }

        // Rule 1: everything matched cleanly.
        return DoseEvaluationOutcome.Valid();
    }
}
