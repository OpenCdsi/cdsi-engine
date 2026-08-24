/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.Models;
using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Pipeline;

/// <summary>One administered record's outcome against whichever target dose it was (or wasn't) evaluated against.</summary>
public sealed record DoseEvaluationRecord(AntigenAdministered AdministeredDose, int? TargetDoseNumber, TargetDoseEvaluationResult Result);

public sealed class SeriesHistoryResult
{
    public required IReadOnlyList<DoseEvaluationRecord> DoseResults { get; init; }
    public required IReadOnlyList<EvaluatedAntigenDose> AllEvaluatedDoses { get; init; }

    /// <summary>Null if every target dose was satisfied/skipped (series complete) - otherwise the dose number that still needs to be forecast.</summary>
    public required int? CurrentTargetDoseNumber { get; init; }
    public bool SeriesComplete => CurrentTargetDoseNumber is null;
}

/// <summary>
/// §4.4 Evaluate and Forecast All Relevant Patient Series - specifically the "Evaluate
/// Immunization History" sub-process (Figure 4-6), implementing its exact 7-step two-pointer
/// algorithm over target doses and antigen-administered records.
///
/// RECURRING DOSE (§4.4 step 5) is now implemented. The spec's own text: on satisfying a target
/// dose flagged recurring, "initialize a new target dose identical to the current target dose...
/// immediately following the current target dose" and move to THAT clone next, rather than
/// advancing to whatever's genuinely next in the series. This codebase achieves the identical
/// logical effect without ever mutating or growing the target-dose array: when a recurring
/// target dose is Satisfied, `targetIdx` simply doesn't advance - the SAME target dose (with its
/// own already-general interval/age rules, typically `fromPrevious`) gets re-evaluated against
/// the NEXT administered record, using the just-updated `targetDoseSatisfiedDates` entry as the
/// new reference point. A "clone inserted after the original" and "the same slot re-used
/// in-place" are observationally identical here, since nothing else in the array shifts either
/// way. Confirmed against real data before implementing: every one of the 29 real recurring
/// doses (Td boosters, annual COVID, occupational rabies exposure, etc.) is the LAST target dose
/// in its series, so a genuinely recurring series is now correctly NEVER "complete" -
/// `CurrentTargetDoseNumber` stays pinned on the recurring dose indefinitely, exactly matching
/// the real-world fact that Td boosters, for instance, never stop being due every ~10 years.
///
/// ONE THING STILL NOT SPEC-GROUNDED, FLAGGED AS AN INFERENCE (the §4.4 algorithm only discusses
/// "Satisfied" vs "Not Satisfied" - it predates/doesn't address Table 6-11's "Skipped" status):
/// a Skipped target dose advances the target-dose pointer WITHOUT consuming the
/// administered-dose pointer - the administered record remains available to be tried against
/// the next target dose, since Skipped means "this target dose didn't need this dose at all,"
/// not "this dose satisfied it." This applies even to a recurring dose that gets Skipped - the
/// spec's own step 5 text only triggers recurrence checking after step 4a (Satisfied), so a
/// Skipped recurring dose is treated the same as any other Skipped dose (advance, don't clone).
/// </summary>
public static class EvaluateSeriesHistory
{
    /// <param name="antigenAdministeredRecords">This antigen's own records only, ascending date order (as produced by OrganizeImmunizationHistory).</param>
    /// <param name="priorEvaluatedDosesFromOtherAntigens">Patient-wide history already evaluated from OTHER antigens/series, needed only for cross-antigen Vaccine Conflict (§6.7). Pass empty if evaluating in isolation.</param>
    public static SeriesHistoryResult Execute(
        Patient patient,
        AntigenSeries series,
        IReadOnlyList<AntigenAdministered> antigenAdministeredRecords,
        IReadOnlyList<EvaluatedAntigenDose> priorEvaluatedDosesFromOtherAntigens,
        IReadOnlyDictionary<string, IReadOnlyList<VaccineConflictRule>> conflictsByImpactedCvx,
        Func<string?, bool> resolveCompletedSeries)
    {
        var targetDoses = series.SeriesDoses.OrderBy(d => d.DoseNumber).ToArray();
        var evaluatedThisAntigen = new List<EvaluatedAntigenDose>();
        var targetDoseSatisfiedDates = new Dictionary<int, DateOnly>();
        var doseResults = new List<DoseEvaluationRecord>();

        var targetIdx = 0;
        var adminIdx = 0;

        while (targetIdx < targetDoses.Length && adminIdx < antigenAdministeredRecords.Count)
        {
            var targetDose = targetDoses[targetIdx];
            var adminRecord = antigenAdministeredRecords[adminIdx];

            var priorAllAntigens = priorEvaluatedDosesFromOtherAntigens.Concat(evaluatedThisAntigen).ToArray();

            var result = EvaluateDoseAgainstTargetDose.Execute(
                patient, adminRecord.SourceDose, targetDose,
                evaluatedThisAntigen, priorAllAntigens, targetDoseSatisfiedDates,
                conflictsByImpactedCvx, resolveCompletedSeries);

            doseResults.Add(new DoseEvaluationRecord(adminRecord, targetDose.DoseNumber, result));

            if (result.TargetDoseStatus == TargetDoseStatus.Satisfied)
            {
                evaluatedThisAntigen.Add(new EvaluatedAntigenDose(
                    adminRecord.Antigen, adminRecord.Cvx, adminRecord.DateAdministered, result.EvaluationStatus, targetDose.DoseNumber));
                targetDoseSatisfiedDates[targetDose.DoseNumber] = adminRecord.DateAdministered;

                adminIdx++; // step 7 - this record is consumed either way, so advance unconditionally

                // step 5/6: a recurring target dose stays in place (re-evaluated against the
                // next administered record, using the reference date just updated above) instead
                // of advancing to a genuinely different target dose - see class doc comment.
                if (!targetDose.IsRecurringDose)
                {
                    targetIdx++;
                }
            }
            else if (result.TargetDoseStatus == TargetDoseStatus.Skipped)
            {
                targetIdx++; // INFERENCE - see class doc comment
                // adminIdx deliberately NOT advanced - record remains for the next target dose.
            }
            else // NotSatisfied
            {
                evaluatedThisAntigen.Add(new EvaluatedAntigenDose(
                    adminRecord.Antigen, adminRecord.Cvx, adminRecord.DateAdministered, result.EvaluationStatus, null));
                adminIdx++; // step 7 - target dose stays the same, try the next administered record
            }
        }

        // Step 6a: if the target dose collection is exhausted, any remaining antigen
        // administered records get evaluation status 'Extraneous', not just left unprocessed.
        if (targetIdx >= targetDoses.Length)
        {
            for (; adminIdx < antigenAdministeredRecords.Count; adminIdx++)
            {
                var record = antigenAdministeredRecords[adminIdx];
                var extraneousResult = TargetDoseEvaluationResult.NotSatisfied(EvaluationStatus.Extraneous, "Series already complete");
                doseResults.Add(new DoseEvaluationRecord(record, null, extraneousResult));
                evaluatedThisAntigen.Add(new EvaluatedAntigenDose(record.Antigen, record.Cvx, record.DateAdministered, EvaluationStatus.Extraneous, null));
            }
        }

        return new SeriesHistoryResult
        {
            DoseResults = doseResults,
            AllEvaluatedDoses = evaluatedThisAntigen,
            CurrentTargetDoseNumber = targetIdx < targetDoses.Length ? targetDoses[targetIdx].DoseNumber : null
        };
    }
}
