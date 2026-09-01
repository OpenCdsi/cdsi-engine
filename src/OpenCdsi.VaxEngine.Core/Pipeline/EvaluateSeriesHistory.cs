/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Evaluation;
using OpenCdsi.VaxEngine.Core.Models;
using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Core.Pipeline;

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
///
/// A SECOND, REAL GAP FOUND AND FIXED - not spec text this time, but the real CDC supporting data
/// itself: the §4.4 algorithm's own literal text ("if the antigen administered collection is
/// empty, the evaluation process... ends") means Conditional Skip (§6.2) can only ever be
/// evaluated from WITHIN this loop, since Table 6-6's decision only runs when there's an
/// administered record to evaluate a target dose against. But the real Pertussis/Diphtheria/
/// Tetanus data has standalone, dose-count-independent Evaluation-context Age conditions on
/// Doses 1-6 (confirmed by reading the real XML directly, `&lt;set&gt;`-by-`&lt;set&gt;`, after an earlier,
/// less careful reading wrongly merged two separate OR'd sets into one AND'd condition) - Doses
/// 1-3/5 skip at Age >= 7 years, Dose 4 at Age >= 4 years, Dose 6 unconditionally at Age >= 7
/// years too. Dose 7's own age window (`minAge: 7 years`, confirmed identical on both the
/// standard and "start at 12 months" series) is exactly the age-anchored recommendation a real
/// catch-up patient needs - but a genuinely zero-dose (or partially-vaccinated-then-exhausted)
/// patient's `CurrentTargetDoseNumber` could never reach it, since the loop above only advances
/// the pointer in step with an administered record actually being consumed.
///
/// Fixed with a second pass, opt-in via the new `assessmentDate` parameter (defaulting to null,
/// so every existing caller that doesn't pass one keeps the exact prior behavior - this project's
/// established additive-change pattern, same as `GeneratePatientForecast.ExecuteWithDoseDetail`):
/// after the main loop settles, wherever it landed, keep advancing past any remaining target
/// dose whose Evaluation-context Conditional Skip is satisfied using the patient's CURRENT age
/// (the assessment date as reference, since there's no administered dose to anchor to - the
/// closest real analogue to "if this patient walked in today, would this target dose even apply
/// to them"). This handles the zero-dose case AND the "ran out of administered records partway
/// through a still-skippable stretch" case the same way, since both share the identical
/// structural gap.
///
/// A THIRD GAP was investigated (real conformance cases 2020-0004/2020-0005: adult patients
/// starting/continuing DTaP/Tdap/Td catch-up with exactly one prior valid dose, still forecasting
/// "today" instead of the corpus's expected date) and an explicit Dose-7 "auto-satisfy" ASSUMPTION
/// was implemented, then REVERTED after real dotnet test execution disproved the trace it was
/// built on: the main loop's OWN pre-existing per-dose Age skip (documented in the second gap
/// above) already advances a single adult-administered dose straight through Doses 1-6 to Dose 7
/// without any fast-forward pass involved at all, so the auto-satisfy logic never even applied to
/// the cases it was designed for - and its presence correlated with a net increase in conformance
/// failures elsewhere (255 -> 262) that wasn't confirmed safe. 2020-0004/2020-0005 remain
/// genuinely unresolved; the real explanation is now believed to live in how Diphtheria or
/// Tetanus behave differently from Pertussis for the same dose, or in the multi-antigen merge -
/// not in anything this class does per-antigen. Flagged here rather than silently dropped, so a
/// future attempt at this doesn't have to rediscover the same dead end.
/// </summary>
public static class EvaluateSeriesHistory
{
    /// <param name="antigenAdministeredRecords">This antigen's own records only, ascending date order (as produced by OrganizeImmunizationHistory).</param>
    /// <param name="priorEvaluatedDosesFromOtherAntigens">Patient-wide history already evaluated from OTHER antigens/series, needed only for cross-antigen Vaccine Conflict (§6.7). Pass empty if evaluating in isolation.</param>
    /// <param name="assessmentDate">Opt-in: when supplied, an additional pass after the main loop advances past any remaining target dose whose Evaluation-context Conditional Skip is satisfied using the patient's current age at this date - see the class doc comment's second gap. Null (the default) preserves the exact prior behavior for callers that don't need this.</param>
    public static SeriesHistoryResult Execute(
        Patient patient,
        AntigenSeries series,
        IReadOnlyList<AntigenAdministered> antigenAdministeredRecords,
        IReadOnlyList<EvaluatedAntigenDose> priorEvaluatedDosesFromOtherAntigens,
        IReadOnlyDictionary<string, IReadOnlyList<VaccineConflictRule>> conflictsByImpactedCvx,
        Func<string?, bool> resolveCompletedSeries,
        DateOnly? assessmentDate = null)
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

        // Second pass - see class doc comment's "second gap": fast-forward past any remaining
        // target dose whose Evaluation-context Conditional Skip is satisfied given the patient's
        // CURRENT age, wherever the main loop above left off (targetIdx 0 for a genuinely
        // zero-dose patient, or wherever administered records ran out for anyone else). No
        // DoseEvaluationRecord is added for a fast-forwarded dose - nothing was administered to
        // record an outcome for; only CurrentTargetDoseNumber below reflects the new position.
        if (assessmentDate is DateOnly today)
        {
            var priorForSkip = evaluatedThisAntigen.Select(EvaluateDoseAgainstTargetDose.MapToPriorDoseForSkipOrConflict).ToArray();
            while (targetIdx < targetDoses.Length)
            {
                var candidateDose = targetDoses[targetIdx];
                var canSkip = EvaluateConditionalSkip.CanBeSkipped(
                    patient.DateOfBirth, today, ConditionalSkipContext.Evaluation,
                    candidateDose.ConditionalSkipInstances, priorForSkip, resolveCompletedSeries);
                if (!canSkip)
                {
                    break;
                }
                targetIdx++;
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
