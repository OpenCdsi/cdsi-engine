/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Common;
using OpenCdsi.VaxEngine.Core.Evaluation;
using OpenCdsi.VaxEngine.Core.Models;
using OpenCdsi.VaxEngine.Core.ReferenceData;

namespace OpenCdsi.VaxEngine.Core.Pipeline;

/// <summary>One relevant patient series within a single series group, bundled with its already-computed Chapter 6 evaluation and Chapter 7 forecast - the raw material §8.1-§8.7 need.</summary>
public sealed record SeriesGroupMember(AntigenSeries Series, SeriesHistoryResult SeriesHistory, PatientSeriesForecastResult Forecast);

/// <summary>
/// Runs the full §8.1-§8.7 pipeline (Pre-Filter → Identify One Prioritized shortcut → Classify
/// Scorable → whichever point-scoring table applies → Select Prioritized) for ONE series group,
/// producing that group's single prioritized patient series. This is deliberately scoped to one
/// group at a time - §8.8's cross-group "Determine Best" logic (which needs every group's
/// prioritized series for an antigen, cross-referenced via `equivalentSeriesGroups`) is a
/// separate, further orchestration layer on top of this one, not included here.
///
/// All members passed in must belong to the SAME series group (same antigen, same
/// SeriesGroupInfo.SeriesGroup) - this function doesn't do that grouping itself.
/// </summary>
public static class SelectPrioritizedPatientSeriesForGroup
{
    public static AntigenSeries? Execute(IReadOnlyList<SeriesGroupMember> members, DateOnly dateOfBirth, DateOnly assessmentDate)
    {
        if (members.Count == 0)
        {
            return null;
        }

        // §8.1 SELECTB-24: candidate scorable.
        var contraindicatedFlags = members.Select(m => m.Forecast.Status == PatientSeriesStatus.Contraindicated).ToArray();
        var isCandidateScorable = contraindicatedFlags
            .Select(flag => PreFilterPatientSeries.IsCandidateScorablePatientSeries(flag, contraindicatedFlags))
            .ToArray();

        // §8.1 SELECTSCORE-2: actually scorable.
        var scorableCandidates = members.Select((m, i) => new ScorableSeriesCandidate(
            m.Series, contraindicatedFlags[i], m.Forecast.Status,
            ValidDoseCount(m.SeriesHistory), EarliestValidDoseDate(m.SeriesHistory))).ToArray();

        var isScorable = scorableCandidates
            .Select((c, i) => PreFilterPatientSeries.IsScorablePatientSeries(c, isCandidateScorable[i], scorableCandidates, dateOfBirth))
            .ToArray();

        var scorableMembers = members.Where((_, i) => isScorable[i]).ToArray();
        if (scorableMembers.Length == 0)
        {
            return DefaultSeries(members);
        }

        // §8.2 shortcut - resolves most cases without needing the full scoring tables.
        var completeMembers = scorableMembers.Where(m => ClassifyScorablePatientSeries.IsCompletePatientSeries(m.Forecast.Status)).ToArray();
        var inProcessMembers = scorableMembers.Where(m => ClassifyScorablePatientSeries.IsInProcessPatientSeries(HasSatisfiedTargetDose(m.SeriesHistory), m.Forecast.Status)).ToArray();

        var shortcutWinner = IdentifyOnePrioritizedPatientSeries.Execute(
            scorableMembers.Select(m => m.Series).ToArray(), DefaultSeries(members),
            completeMembers.Select(m => m.Series).ToArray(), inProcessMembers.Select(m => m.Series).ToArray());
        if (shortcutWinner is not null)
        {
            return shortcutWinner;
        }

        // §8.3: which subset gets scored, and by which of §8.4/8.5/8.6.
        var allZeroValidDoses = scorableCandidates.All(c => c.ValidDoseCount == 0);
        var category = ClassifyScorablePatientSeries.DetermineScoringCategory(completeMembers.Length, inProcessMembers.Length, allZeroValidDoses);

        var scored = category switch
        {
            ScoringCategory.CompletePatientSeries => ScoreComplete(completeMembers),
            ScoringCategory.InProcessPatientSeries => ScoreInProcess(inProcessMembers, dateOfBirth, assessmentDate),
            ScoringCategory.NoValidDoses => ScoreNoValidDoses(scorableMembers, dateOfBirth, assessmentDate),
            _ => Array.Empty<ScoredPatientSeries>() // Undetermined - Table 8-5 names no outcome; nothing to score.
        };

        // §8.7: sum (already done by construction) and pick the winner, tie-broken by seriesPreference.
        return SelectPrioritizedPatientSeries.Execute(scored);
    }

    private static AntigenSeries? DefaultSeries(IReadOnlyList<SeriesGroupMember> members) =>
        members.FirstOrDefault(m => m.Series.SeriesGroupInfo.IsDefaultSeries)?.Series;

    private static bool HasSatisfiedTargetDose(SeriesHistoryResult history) =>
        history.AllEvaluatedDoses.Any(d => d.SatisfiedTargetDoseNumber is not null);

    private static int ValidDoseCount(SeriesHistoryResult history) =>
        ClassifyScorablePatientSeries.CountValidDoses(history.DoseResults.Select(r => r.Result.TargetDoseStatus).ToArray());

    private static DateOnly? EarliestValidDoseDate(SeriesHistoryResult history)
    {
        var dates = history.DoseResults
            .Where(r => r.Result.TargetDoseStatus == TargetDoseStatus.Satisfied)
            .Select(r => r.AdministeredDose.DateAdministered)
            .ToArray();
        return dates.Length > 0 ? dates.Min() : null;
    }

    private static IReadOnlyList<ScoredPatientSeries> ScoreComplete(IReadOnlyList<SeriesGroupMember> completeMembers)
    {
        var counts = completeMembers.Select(m => ValidDoseCount(m.SeriesHistory)).ToArray();
        return completeMembers.Select((m, i) => new ScoredPatientSeries(m.Series, ScoreCompletePatientSeries.Execute(counts[i], counts))).ToArray();
    }

    private static IReadOnlyList<ScoredPatientSeries> ScoreInProcess(IReadOnlyList<SeriesGroupMember> inProcessMembers, DateOnly dateOfBirth, DateOnly assessmentDate)
    {
        var candidates = inProcessMembers.Select(m => BuildInProcessCandidate(m, dateOfBirth, assessmentDate)).ToArray();
        return inProcessMembers.Select((m, i) => new ScoredPatientSeries(m.Series, ScoreInProcessPatientSeries.Execute(candidates[i], candidates))).ToArray();
    }

    private static IReadOnlyList<ScoredPatientSeries> ScoreNoValidDoses(IReadOnlyList<SeriesGroupMember> members, DateOnly dateOfBirth, DateOnly assessmentDate)
    {
        var candidates = members.Select(m => BuildNoValidDosesCandidate(m, dateOfBirth, assessmentDate)).ToArray();
        return members.Select((m, i) => new ScoredPatientSeries(m.Series, ScoreNoValidDosesPatientSeries.Execute(candidates[i], candidates))).ToArray();
    }

    private static InProcessSeriesCandidate BuildInProcessCandidate(SeriesGroupMember member, DateOnly dateOfBirth, DateOnly assessmentDate)
    {
        var evaluationStatuses = member.SeriesHistory.DoseResults
            .Where(r => r.Result.EvaluationStatus is not null)
            .Select(r => r.Result.EvaluationStatus!.Value)
            .ToArray();

        var (finishDate, maxAgeDate) = ForecastFinishAndMaxAgeDates(member, dateOfBirth, assessmentDate);
        var notSatisfiedCount = member.SeriesHistory.DoseResults.Count(r => r.Result.TargetDoseStatus == TargetDoseStatus.NotSatisfied);

        return new InProcessSeriesCandidate(member.Series.SeriesGroupInfo.IsProductPath, evaluationStatuses, finishDate, maxAgeDate, notSatisfiedCount);
    }

    private static NoValidDosesSeriesCandidate BuildNoValidDosesCandidate(SeriesGroupMember member, DateOnly dateOfBirth, DateOnly assessmentDate)
    {
        var (finishDate, maxAgeDate) = ForecastFinishAndMaxAgeDates(member, dateOfBirth, assessmentDate);
        var isCompletable = member.Forecast.Dates is not null && finishDate < maxAgeDate;
        var startDate = member.Series.SeriesGroupInfo.MinAgeToStartDate(dateOfBirth);

        return new NoValidDosesSeriesCandidate(member.Series.SeriesGroupInfo.IsProductPath, isCompletable, startDate);
    }

    /// <summary>SELECTB-12 (forecast finish date) and the "maximum age date of the last target dose" both series-scoring tables need. Returns a finish date far in the future (never completable) when the series isn't currently forecasting - a Contraindicated-but-still-scorable series has no forecast to complete.</summary>
    private static (DateOnly FinishDate, DateOnly MaxAgeDate) ForecastFinishAndMaxAgeDates(SeriesGroupMember member, DateOnly dateOfBirth, DateOnly assessmentDate)
    {
        var lastDose = member.Series.SeriesDoses.OrderByDescending(d => d.DoseNumber).First();
        var applicableAge = lastDose.AgeRules.Count > 0 ? TemporalRuleSelector.SelectApplicable(lastDose.AgeRules, assessmentDate) : null;
        var lastDoseMaxAgeDate = applicableAge?.MaxAgeDate(dateOfBirth) ?? new DateOnly(2999, 12, 31);

        if (member.Forecast.Dates is null)
        {
            return (new DateOnly(2999, 12, 31), lastDoseMaxAgeDate);
        }

        var remainingDoses = member.SeriesHistory.CurrentTargetDoseNumber is int current
            ? member.Series.SeriesDoses.Where(d => d.DoseNumber >= current).ToArray()
            : Array.Empty<SeriesDose>();

        var finishDate = ForecastFinishDate.Calculate(member.Forecast.Dates.EarliestDate, remainingDoses);
        return (finishDate, lastDoseMaxAgeDate);
    }
}
