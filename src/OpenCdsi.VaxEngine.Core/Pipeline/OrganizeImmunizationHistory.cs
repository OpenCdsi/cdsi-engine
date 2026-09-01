/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Models;
using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Core.Pipeline;

/// <summary>
/// §4.2 Organize Immunization History: explodes each raw administered vaccine dose into
/// one AntigenAdministered record per associated antigen, using the CVX-to-antigen map.
///
/// Two things this deliberately gets right, because they're easy to miss on a first pass:
///  1. A single dose can map to MULTIPLE antigens (e.g. DTaP -> Diphtheria + Tetanus + Pertussis) —
///     this is the common case (288/290 associations in the current data are unconditional).
///  2. A single CVX can map to DIFFERENT antigens depending on the patient's age at the date
///     administered (currently only CVX 121, Zoster live: Varicella below 50, Zoster at/above 50).
///     This is rare but structurally mandatory to check for every dose, since there's no way to
///     know in advance which CVX codes are age-gated without consulting the map.
/// </summary>
public static class OrganizeImmunizationHistory
{
    public static IReadOnlyList<AntigenAdministered> Execute(
        Patient patient,
        IReadOnlyList<VaccineDoseAdministered> dosesAdministered,
        IReadOnlyDictionary<string, CvxMapEntry> cvxToAntigen)
    {
        var records = new List<AntigenAdministered>();

        foreach (var dose in dosesAdministered)
        {
            if (!cvxToAntigen.TryGetValue(dose.Cvx, out var mapEntry))
            {
                // An unmapped CVX is a data-quality problem worth surfacing, not silently dropping.
                // In a production system this should go to a review queue rather than throw —
                // left as an explicit extension point rather than guessed at here.
                continue;
            }

            foreach (var association in mapEntry.Associations)
            {
                if (association.AppliesAt(patient.DateOfBirth, dose.DateAdministered))
                {
                    records.Add(new AntigenAdministered
                    {
                        Antigen = association.Antigen,
                        DateAdministered = dose.DateAdministered,
                        Cvx = dose.Cvx,
                        SourceDose = dose
                    });
                }
            }
        }

        // Step 3 of §4.2: sort by antigen, then ascending date administered within antigen.
        return records
            .OrderBy(r => r.Antigen, StringComparer.Ordinal)
            .ThenBy(r => r.DateAdministered)
            .ToArray();
    }
}
