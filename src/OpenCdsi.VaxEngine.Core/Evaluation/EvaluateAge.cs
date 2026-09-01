/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Common;
using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Core.Evaluation;

/// <summary>
/// §6.4 Evaluate Age (Table 6-15). Validates the age at administration of a vaccine dose
/// against the target dose's defined age range.
///
/// Reason strings ("Too young", "Grace period", "Too old") are copied verbatim from the
/// spec's own Table 6-15 outcome text, not invented rule codes — the audit trail should read
/// the way the document does.
/// </summary>
public static class EvaluateAge
{
    public static DoseEvaluationOutcome Execute(DateOnly dateOfBirth, DateOnly dateAdministered, IReadOnlyList<AgeRule> ageRules)
    {
        // §6.4: "In cases where a target dose does not specify age attributes, the age at
        // administration is considered 'valid.'"
        if (ageRules.Count == 0)
        {
            return DoseEvaluationOutcome.Valid();
        }

        var applicable = TemporalRuleSelector.SelectApplicable(ageRules, dateAdministered);

        var absMinAgeDate = applicable.AbsMinAgeDate(dateOfBirth);   // CALCDTAGE-5
        var minAgeDate = applicable.MinAgeDate(dateOfBirth);         // CALCDTAGE-4
        var maxAgeDate = applicable.MaxAgeDate(dateOfBirth);         // CALCDTAGE-1

        // Table 6-15, in order:
        if (dateAdministered < absMinAgeDate)
        {
            return DoseEvaluationOutcome.NotValid("Too young");
        }
        if (dateAdministered < minAgeDate)
        {
            return DoseEvaluationOutcome.Valid("Grace period");
        }
        if (dateAdministered < maxAgeDate)
        {
            return DoseEvaluationOutcome.Valid();
        }
        // dateAdministered >= maxAgeDate
        return DoseEvaluationOutcome.NotValid("Too old", isExtraneous: true);
    }
}
