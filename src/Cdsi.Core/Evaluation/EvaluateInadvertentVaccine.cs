/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Core.Evaluation;

/// <summary>
/// §6.3 Evaluate For Inadvertent Vaccine (Table 6-13). Simple set membership: is the CVX of
/// the administered dose one of the target dose's listed "inadvertent" vaccine types? No
/// temporal versioning, no reference-date resolution — the simplest of the Chapter 6 components.
/// </summary>
public static class EvaluateInadvertentVaccine
{
    public static DoseEvaluationOutcome Execute(string administeredCvx, IReadOnlyList<string> inadvertentVaccineCvxCodes)
    {
        if (inadvertentVaccineCvxCodes.Contains(administeredCvx))
        {
            return DoseEvaluationOutcome.NotValid("Inadvertent Administration");
        }
        return DoseEvaluationOutcome.Valid();
    }
}
