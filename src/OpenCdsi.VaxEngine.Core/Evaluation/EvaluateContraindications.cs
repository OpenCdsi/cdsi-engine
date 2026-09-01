/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Models;
using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Core.Evaluation;

/// <summary>Table 7-5/7-6's three-way outcome: applies / does not apply / cannot be determined (flag for clinician review, per the spec's explicit "minimize missed doses" instruction - same tri-state pattern §5.1's indication matching already established.</summary>
public enum ContraindicationApplicability { Applies, DoesNotApply, Unresolved }

/// <summary>
/// §7.3 Determine Contraindications (Tables 7-5, 7-6, 7-7).
/// </summary>
public static class EvaluateContraindications
{
    /// <summary>Table 7-5: does an antigen-level contraindication apply? Age is checked first - it dominates regardless of observation/adverse-reaction status (Table 7-5's own "age=No" column covers every other condition value).</summary>
    public static ContraindicationApplicability EvaluateAntigenContraindication(
        Patient patient, DateOnly assessmentDate, AntigenContraindication contraindication)
    {
        var beginDate = contraindication.BeginAgeDate(patient.DateOfBirth);
        var endDate = contraindication.EndAgeDate(patient.DateOfBirth);
        if (assessmentDate < beginDate || assessmentDate >= endDate)
        {
            return ContraindicationApplicability.DoesNotApply;
        }

        return ResolveObservationOrReactionState(patient, contraindication.ObservationCode);
    }

    /// <summary>Table 7-6: does a vaccine-level contraindication apply to a specific candidate vaccine (identified by CVX - typically the preferable vaccine being considered for forecast)? Vaccine-type match is checked first since age is scoped to the matched ContraindicatedVaccine entry, not the contraindication as a whole.</summary>
    public static ContraindicationApplicability EvaluateVaccineContraindication(
        Patient patient, DateOnly assessmentDate, string candidateVaccineCvx, VaccineContraindication contraindication)
    {
        var matchingVaccine = contraindication.ContraindicatedVaccines.FirstOrDefault(cv => cv.Cvx == candidateVaccineCvx);
        if (matchingVaccine is null)
        {
            return ContraindicationApplicability.DoesNotApply;
        }

        var beginDate = matchingVaccine.BeginAgeDate(patient.DateOfBirth);
        var endDate = matchingVaccine.EndAgeDate(patient.DateOfBirth);
        if (assessmentDate < beginDate || assessmentDate >= endDate)
        {
            return ContraindicationApplicability.DoesNotApply;
        }

        return ResolveObservationOrReactionState(patient, contraindication.ObservationCode);
    }

    /// <summary>Table 7-7: is a relevant patient series contraindicated? Contraindicated if ANY antigen-level contraindication applies, OR if EVERY preferable vaccine for the series has at least one applicable vaccine-level contraindication. Both inputs are caller-computed (from EvaluateAntigenContraindication/EvaluateVaccineContraindication over the antigen's full contraindication list and the series' preferable vaccines) - "the preferable vaccines for a relevant patient series" isn't a concept this codebase derives internally yet.</summary>
    public static bool IsContraindicatedPatientSeries(bool anyAntigenContraindicationApplies, bool allPreferableVaccinesHaveAnApplicableContraindication)
    {
        return anyAntigenContraindicationApplies || allPreferableVaccinesHaveAnApplicableContraindication;
    }

    private static ContraindicationApplicability ResolveObservationOrReactionState(Patient patient, string code)
    {
        if (patient.ActiveObservations.Any(o => o.Code == code) || patient.AdverseReactions.Any(o => o.Code == code))
        {
            return ContraindicationApplicability.Applies;
        }
        if (patient.UnresolvedObservationCodes.Contains(code))
        {
            return ContraindicationApplicability.Unresolved;
        }
        return ContraindicationApplicability.DoesNotApply;
    }
}
