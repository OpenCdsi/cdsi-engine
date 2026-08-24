/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>
/// CALCDTCONFLICT-3: "the forecast conflict end date of a vaccine type conflict that impacts a
/// next target dose" - the forward-looking counterpart to §6.7's own CALCDTCONFLICT-1/2 (which
/// look BACKWARD, from a just-administered dose to a possibly-conflicting PRIOR dose). This
/// looks FORWARD: given a target dose's own preferable vaccines (any of which could be the
/// "impacted" vaccine type) and the patient's full cross-antigen prior dose history (any of
/// which could be a "conflicting" vaccine type for one of those), when would that conflict
/// actually clear?
///
/// The underlying reference data (`VaccineConflictRule`, keyed by impacted CVX) is the exact
/// same table §6.7 already uses - this is a different WALK over it, not new reference data.
///
/// One real difference from CALCDTCONFLICT-2 worth noting: CALCDTCONFLICT-3's own text has no
/// "Valid vs Not Valid" branching on the prior dose's evaluation status the way CALCDTCONFLICT-2
/// does (minimum end interval if Valid/no-status, full end interval otherwise) - it uses the
/// plain `ConflictEndInterval` uniformly. Implemented literally as written, not assumed to
/// mirror CALCDTCONFLICT-2's more elaborate branching just because the two rules are related.
///
/// The retired `CALCDTLIVE-4` rule ID that Table 7-9's own attribute list still references for
/// this exact attribute ("Latest Conflict End Interval Date") was formally replaced by this rule
/// per the spec's own change log ("This rule is no longer used") - a real, if minor, internal
/// documentation inconsistency in the spec itself, not something this codebase needs to resolve
/// beyond noting it here.
/// </summary>
public static class ForecastConflictEndDate
{
    public static DateOnly? LatestConflictEndDate(
        IReadOnlyList<string> targetDosePreferableVaccineCvxCodes,
        IReadOnlyList<PriorVaccineDoseAdministered> priorDosesAllAntigens,
        IReadOnlyDictionary<string, IReadOnlyList<VaccineConflictRule>> conflictsByImpactedCvx)
    {
        DateOnly? latest = null;

        foreach (var impactedCvx in targetDosePreferableVaccineCvxCodes.Distinct())
        {
            if (!conflictsByImpactedCvx.TryGetValue(impactedCvx, out var applicableRules))
            {
                continue;
            }

            foreach (var prior in priorDosesAllAntigens)
            {
                var rule = applicableRules.FirstOrDefault(r => r.ConflictingCvx == prior.Cvx);
                if (rule is null)
                {
                    continue;
                }

                var conflictEndDate = rule.ConflictEndInterval.AddTo(prior.DateAdministered);
                if (latest is null || conflictEndDate > latest)
                {
                    latest = conflictEndDate;
                }
            }
        }

        return latest;
    }
}
