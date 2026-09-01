/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Models;
using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Core.Evaluation;

/// <summary>
/// §7.2 Determine Evidence Of Immunity (Table 7-3). Assesses whether the patient already has
/// presumed immunity to the target disease, independent of vaccination history - either via a
/// documented clinical finding, or via a "born before a defined date" presumption that can
/// itself be overridden by an exclusion condition (e.g. occupational exposure risk) or a
/// birth-country mismatch.
///
/// The patient's "history contains a guideline" / "has an exclusion condition" are both modeled
/// via PatientObservation.Code matching, reusing the same generic coded-fact pattern §5.1's
/// indication matching already established - immunity guidelines and exclusion conditions are
/// just another kind of code a real EHR feed would supply as an active observation.
/// </summary>
public static class EvaluateEvidenceOfImmunity
{
    public static bool HasEvidenceOfImmunity(Patient patient, AntigenImmunityData immunityData)
    {
        // Rule 1: any documented clinical history guideline is immediately sufficient.
        var hasClinicalHistory = immunityData.ClinicalHistoryGuidelines
            .Any(g => patient.ActiveObservations.Any(o => o.Code == g.GuidelineCode));
        if (hasClinicalHistory)
        {
            return true;
        }

        // Rules 2-5: the birth-date presumption, evaluated per applicable rule.
        foreach (var rule in immunityData.BirthDateRules)
        {
            if (patient.DateOfBirth >= rule.ImmunityBirthDate)
            {
                continue; // Rule 5: not born before the immunity birth date - this rule doesn't grant immunity.
            }

            var hasExclusion = rule.Exclusions.Any(ex => patient.ActiveObservations.Any(o => o.Code == ex.ExclusionCode));
            if (hasExclusion)
            {
                continue; // Rule 2: an exclusion condition overrides the presumption.
            }

            // Rules 3/4: birth country must match when the rule specifies one; an unspecified
            // BirthCountry means the presumption isn't country-restricted. INFERENCE (Table 7-3
            // doesn't explicitly address an unknown patient country of birth): treated as a
            // mismatch, not a match - a conservative default rather than assuming the country
            // requirement is satisfied when it can't be confirmed.
            if (rule.BirthCountry is not null && patient.CountryOfBirth != rule.BirthCountry)
            {
                continue; // Rule 4: country mismatch - presumption doesn't apply.
            }

            return true; // Rule 3 (or an unrestricted rule): presumption grants immunity.
        }

        return false;
    }
}
