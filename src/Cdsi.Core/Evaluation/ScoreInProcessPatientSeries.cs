/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Core.Evaluation;

/// <summary>What §8.5's Table 8-9 scoring needs to know about one scorable patient series in the group.</summary>
public sealed record InProcessSeriesCandidate(
    bool IsProductPath,
    IReadOnlyList<EvaluationStatus> EvaluationStatuses, // only for target doses that were actually evaluated (excludes Skipped)
    DateOnly ForecastFinishDate,
    DateOnly LastTargetDoseMaxAgeDate,
    int NotSatisfiedTargetDoseCount);

/// <summary>
/// §8.5 In-Process Patient Series scoring (Table 8-9, business rules SELECTB-2/3/5/11/12/23).
/// Five independently-scored conditions, summed. Unlike §8.4's single condition, several of
/// these are genuinely comparative across every scorable series in the group at once (not just
/// this one vs. the group's max/min), so this function takes the full candidate set rather than
/// per-series inputs plus a pre-computed aggregate.
///
/// WORTH KNOWING: SELECTB-5's literal text defines "closest to completion" with a STRICT
/// "less than" comparison against every other series - taken at face value, that boolean alone
/// can never be true for two tied series simultaneously, which would conflict with Table 8-9's
/// own "true for two or more scorable patient series → 0" column. The scoring below doesn't
/// rely on the raw SELECTB-5 boolean for the tie case: it separately detects a tie for the
/// group's minimum not-satisfied-dose-count and scores it 0, so the actual point outcomes match
/// Table 8-9's stated three-way shape (+2 unique closest / 0 tied closest / -2 not closest)
/// rather than inheriting a gap from SELECTB-5's stricter literal wording. Flagged here because
/// the underlying business rule text and the table's outcome shape don't literally line up on
/// their own - the reconciliation is this function's choice, not a quoted rule.
/// </summary>
public static class ScoreInProcessPatientSeries
{
    public static int Execute(InProcessSeriesCandidate candidate, IReadOnlyList<InProcessSeriesCandidate> allCandidatesInGroup)
    {
        var score = 0;

        // Condition 1 (+2/-2): product patient series AND has all valid doses.
        var hasAllValidDoses = candidate.EvaluationStatuses.Count > 0 && candidate.EvaluationStatuses.All(s => s == EvaluationStatus.Valid);
        score += candidate.IsProductPath && hasAllValidDoses ? 2 : -2;

        // Condition 2 (+3/-3): completable (SELECTB-3).
        var isCompletable = candidate.ForecastFinishDate < candidate.LastTargetDoseMaxAgeDate;
        score += isCompletable ? 3 : -3;

        // Condition 3 (+2/0/-2): has the most valid doses (reuses §8.4's counting convention via EvaluationStatuses.Count of Valid, but here "most" is tie-aware, unlike conditions 1/2 which have no tie case).
        var thisValidCount = candidate.EvaluationStatuses.Count(s => s == EvaluationStatus.Valid);
        var maxValidCount = allCandidatesInGroup.Max(c => c.EvaluationStatuses.Count(s => s == EvaluationStatus.Valid));
        if (thisValidCount < maxValidCount)
        {
            score -= 2;
        }
        else
        {
            var tiedAtMax = allCandidatesInGroup.Count(c => c.EvaluationStatuses.Count(s => s == EvaluationStatus.Valid) == maxValidCount);
            score += tiedAtMax == 1 ? 2 : 0;
        }

        // Condition 4 (+2/0/-2): closest to completion (SELECTB-5) - see class doc comment re: strict "<" asymmetry.
        var isClosestToCompletion = allCandidatesInGroup
            .Where(c => c != candidate)
            .All(other => candidate.NotSatisfiedTargetDoseCount < other.NotSatisfiedTargetDoseCount);
        if (isClosestToCompletion)
        {
            score += 2;
        }
        else
        {
            var tiedForFewestNotSatisfied = allCandidatesInGroup.Count(c =>
                c.NotSatisfiedTargetDoseCount == allCandidatesInGroup.Min(x => x.NotSatisfiedTargetDoseCount));
            var thisIsAtMinimum = candidate.NotSatisfiedTargetDoseCount == allCandidatesInGroup.Min(x => x.NotSatisfiedTargetDoseCount);
            score += thisIsAtMinimum && tiedForFewestNotSatisfied > 1 ? 0 : -2;
        }

        // Condition 5 (+1/0/-1): can finish earliest (SELECTB-11) - completable AND finish date <= every other completable series' finish date.
        var completableCandidates = allCandidatesInGroup.Where(c => c.ForecastFinishDate < c.LastTargetDoseMaxAgeDate).ToArray();
        var canFinishEarliest = isCompletable && completableCandidates
            .Where(c => c != candidate)
            .All(other => candidate.ForecastFinishDate <= other.ForecastFinishDate);
        if (canFinishEarliest)
        {
            var tiedEarliest = completableCandidates.Count(c => c.ForecastFinishDate == candidate.ForecastFinishDate);
            score += tiedEarliest == 1 ? 1 : 0;
        }
        else
        {
            score -= 1;
        }

        return score;
    }
}
