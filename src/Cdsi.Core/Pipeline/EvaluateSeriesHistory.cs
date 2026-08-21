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
/// TWO THINGS NOT SPEC-GROUNDED, FLAGGED AS INFERENCES (the §4.4 algorithm only discusses
/// "Satisfied" vs "Not Satisfied" - it predates/doesn't address Table 6-11's "Skipped" status):
///   1. A Skipped target dose advances the target-dose pointer WITHOUT consuming the
///      administered-dose pointer - the administered record remains available to be tried
///      against the next target dose, since Skipped means "this target dose didn't need this
///      dose at all," not "this dose satisfied it."
///   2. Recurring Dose handling (§4.4 step 5) is NOT implemented - the spec gives it barely
///      more than a one-line flag definition with no dedicated decision table, unlike every
///      other component. All target doses are treated as non-recurring. This means series
///      containing a genuinely recurring target dose (Td boosters, annual flu/COVID, some risk
///      series) will evaluate incorrectly past that point - a known, real gap, not a
///      theoretical one.
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
                targetIdx++; // step 6 (Recurring Dose step 5 not implemented - see class doc comment)
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
