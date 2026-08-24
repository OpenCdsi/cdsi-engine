/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

namespace Cdsi.Core.Evaluation;

/// <summary>
/// What §8.6's Table 8-11 scoring needs about one scorable-with-zero-valid-doses series.
///
/// StartDate: SELECTB-14 references "the start date" for a patient series without ever defining
/// it elsewhere in the spec text (checked broadly - it appears nowhere except this one rule).
/// INFERENCE, not spec-grounded: since this scoring path only applies to series with NO valid
/// doses yet (nothing administered/satisfied), "start date" most plausibly means the earliest
/// date this series is even allowed to begin for this patient - i.e. SeriesGroupInfo's own
/// MinAgeToStartDate(dob), which is exactly the kind of reference data that concept would need
/// and which the rule's own "with a start date" phrasing (implying it can be absent) matches,
/// since MinAgeToStart is a genuinely optional field in real data. Nullable here for that reason.
/// </summary>
public sealed record NoValidDosesSeriesCandidate(bool IsProductPath, bool IsCompletable, DateOnly? StartDate);

/// <summary>
/// §8.6 No Valid Doses scoring (Table 8-11, business rules SELECTB-3/12/14/23). Three
/// conditions, summed - smaller than §8.5, but with a deliberate sign INVERSION worth not
/// "fixing": unlike §8.5's condition 1 (product path + all valid doses is REWARDED +2), here
/// being a product patient series is PENALIZED (-1). This makes sense once you notice the
/// context difference - §8.5 rewards staying on a product-specific path you've already
/// committed doses to; §8.6 is scoring series with ZERO doses given yet, where a product-tied
/// path (possible supply/availability constraints) is less preferred than a generic one when
/// starting fresh. Confirmed against the literal table text, not assumed from §8.5's pattern.
/// </summary>
public static class ScoreNoValidDosesPatientSeries
{
    public static int Execute(NoValidDosesSeriesCandidate candidate, IReadOnlyList<NoValidDosesSeriesCandidate> allCandidatesInGroup)
    {
        var score = 0;

        // Condition 1 (+1/0/-1): can start earliest (SELECTB-14). Same reconciliation approach
        // as §8.5's "closest to completion": SELECTB-14's literal wording is a strict "before"
        // that can't be true for two tied series at once, but Table 8-11 has an explicit tied->0
        // column for this condition, so a tie is detected separately rather than inherited as a
        // gap. A series with no StartDate at all can't claim "earliest" - scores -1, same as any
        // other not-true case.
        if (candidate.StartDate is DateOnly thisStart)
        {
            var others = allCandidatesInGroup.Where(c => c != candidate && c.StartDate is not null).ToArray();
            var isUniqueEarliest = others.All(other => thisStart < other.StartDate!.Value);
            if (isUniqueEarliest)
            {
                score += 1;
            }
            else
            {
                var candidatesWithStartDates = allCandidatesInGroup.Where(c => c.StartDate is not null).ToArray();
                var earliestStart = candidatesWithStartDates.Min(c => c.StartDate!.Value);
                var tiedForEarliest = candidatesWithStartDates.Count(c => c.StartDate!.Value == earliestStart);
                score += thisStart == earliestStart && tiedForEarliest > 1 ? 0 : -1;
            }
        }
        else
        {
            score -= 1;
        }

        // Condition 2 (+1/-1): completable (SELECTB-3) - no tie case (Table 8-11 marks the middle column "n/a").
        score += candidate.IsCompletable ? 1 : -1;

        // Condition 3 (-1/+1): product patient series (SELECTB-23) - deliberately inverted sign, see class doc comment.
        score += candidate.IsProductPath ? -1 : 1;

        return score;
    }
}
