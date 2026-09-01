/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Models;
using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Core.Evaluation;

/// <summary>
/// §7.5 FORECASTGUIDANCE-1: administrative guidance text for a forecast, aggregated from three
/// sources - the series' own regimen guidance (always included), indication guidance (only for
/// indications the patient actually has an active observation for), and contraindication
/// guidance (only for contraindications the patient actually has an active observation for).
///
/// Note the rule's exact wording is "active patient observation" - not "active observation OR
/// adverse reaction." Unlike EvaluateContraindications' applicability check (which deliberately
/// checks both buckets, since the underlying data can't distinguish which a given code
/// represents), FORECASTGUIDANCE-1 is specific enough to implement literally: only
/// Patient.ActiveObservations is checked here, not AdverseReactions.
/// </summary>
public static class GenerateForecastGuidance
{
    public static IReadOnlyList<string> Execute(
        AntigenSeries series,
        Patient patient,
        IReadOnlyList<AntigenContraindication> antigenContraindications,
        IReadOnlyList<VaccineContraindication> vaccineContraindications)
    {
        var guidance = new List<string>();

        // Regimen guidance for the series being forecast - always included.
        guidance.AddRange(series.SeriesAdminGuidance);

        // Indication guidance, only where the patient has a matching active observation.
        foreach (var indication in series.Indications)
        {
            if (indication.Guidance is string indicationGuidance &&
                indication.ObservationCode is string indicationCode &&
                patient.ActiveObservations.Any(o => o.Code == indicationCode))
            {
                guidance.Add(indicationGuidance);
            }
        }

        // Contraindication guidance, only where the patient has a matching active observation.
        foreach (var contraindication in antigenContraindications)
        {
            if (contraindication.ContraindicationGuidance is string text &&
                patient.ActiveObservations.Any(o => o.Code == contraindication.ObservationCode))
            {
                guidance.Add(text);
            }
        }
        foreach (var contraindication in vaccineContraindications)
        {
            if (contraindication.ContraindicationGuidance is string text &&
                patient.ActiveObservations.Any(o => o.Code == contraindication.ObservationCode))
            {
                guidance.Add(text);
            }
        }

        return guidance;
    }
}
