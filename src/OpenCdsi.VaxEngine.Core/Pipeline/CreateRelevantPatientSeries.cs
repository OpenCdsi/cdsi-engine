/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Models;
using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Core.Pipeline;

/// <summary>
/// §5.1 Select Relevant Patient Series: determines which of the antigen series defined by the
/// supporting data are appropriate to evaluate/forecast for this patient (Table 5-5).
///
///   - Standard / Evaluation Only series: relevant for every patient of the matching gender.
///   - Risk series: relevant only if the gender matches AND at least one indication
///     unambiguously applies to the patient (Table 5-4).
///
/// Note this runs over the FULL antigen series catalog, not just antigens the patient has
/// existing doses for — a patient with zero HepB doses still needs the HepB series created so
/// dose 1 can be forecast. AntigenAdministered records (from OrganizeImmunizationHistory) are
/// evaluated against these series in the next pipeline stage (Chapter 6), not this one.
/// </summary>
public static class CreateRelevantPatientSeries
{
    private static readonly DateOnly IndicationBeginAgeDefault = new(1900, 1, 1);
    private static readonly DateOnly IndicationEndAgeDefault = new(2999, 12, 31);

    public static RelevantPatientSeriesResult Execute(
        Patient patient,
        IReadOnlyList<AntigenSeries> allSeries,
        DateOnly assessmentDate)
    {
        var relevant = new List<AntigenSeries>();
        var unresolved = new List<UnresolvedIndicationNotification>();

        foreach (var series in allSeries)
        {
            if (!series.AppliesToGender(patient.Gender))
            {
                continue;
            }

            if (series.SeriesType is SeriesType.Standard or SeriesType.EvaluationOnly)
            {
                relevant.Add(series);
                continue;
            }

            // Risk series: Table 5-4 per indication, then "at least one applies" for the series.
            var anyApplies = false;
            var inconclusive = new List<Indication>();

            foreach (var indication in series.Indications)
            {
                var outcome = EvaluateIndication(patient, indication, assessmentDate);
                if (outcome == IndicationOutcome.Applies)
                {
                    anyApplies = true;
                    break; // Table 5-5: only need one indication to apply
                }
                if (outcome == IndicationOutcome.Inconclusive)
                {
                    inconclusive.Add(indication);
                }
            }

            if (anyApplies)
            {
                relevant.Add(series);
            }
            else if (inconclusive.Count > 0)
            {
                foreach (var indication in inconclusive)
                {
                    unresolved.Add(new UnresolvedIndicationNotification
                    {
                        SeriesName = series.SeriesName,
                        Antigen = series.Antigen,
                        ObservationCode = indication.ObservationCode,
                        Description = indication.Description
                    });
                }
            }
            // else: every indication resolved definitively to "No" — series is simply not relevant, no notification.
        }

        return new RelevantPatientSeriesResult
        {
            RelevantSeries = relevant,
            UnresolvedIndications = unresolved
        };
    }

    private enum IndicationOutcome { Applies, DoesNotApply, Inconclusive }

    private static IndicationOutcome EvaluateIndication(Patient patient, Indication indication, DateOnly assessmentDate)
    {
        var beginDate = indication.BeginAge?.AddTo(patient.DateOfBirth) ?? IndicationBeginAgeDefault;
        var endDate = indication.EndAge?.AddTo(patient.DateOfBirth) ?? IndicationEndAgeDefault;
        var ageMatches = assessmentDate >= beginDate && assessmentDate < endDate;

        // Table 5-4, Rule 4: age window failing means "does not apply" regardless of the observation match.
        if (!ageMatches)
        {
            return IndicationOutcome.DoesNotApply;
        }

        var observationState = ResolveObservationState(patient, indication.ObservationCode);
        return observationState switch
        {
            ObservationState.Present => IndicationOutcome.Applies,       // Rule 1
            ObservationState.Absent => IndicationOutcome.DoesNotApply,   // Rule 2
            ObservationState.Unknown => IndicationOutcome.Inconclusive,  // Rule 3
            _ => IndicationOutcome.DoesNotApply
        };
    }

    private enum ObservationState { Present, Absent, Unknown }

    private static ObservationState ResolveObservationState(Patient patient, string? observationCode)
    {
        if (observationCode is null)
        {
            return ObservationState.Absent;
        }
        if (patient.ActiveObservations.Any(o => o.Code == observationCode))
        {
            return ObservationState.Present;
        }
        if (patient.UnresolvedObservationCodes.Contains(observationCode))
        {
            return ObservationState.Unknown;
        }
        return ObservationState.Absent;
    }
}
