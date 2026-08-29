/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>One scorable patient series and the total score awarded to it by whichever of §8.4/8.5/8.6 applied (per §8.3's classification).</summary>
public sealed record ScoredPatientSeries(AntigenSeries Series, int Score);

/// <summary>
/// §8.7 Select Prioritized Patient Series (Table 8-13, SELECTBEST-1/2). The smallest step in
/// the scoring pipeline: SELECTBEST-1 ("the score is the sum of all points awarded") is already
/// satisfied by construction - ScoreCompletePatientSeries/ScoreInProcessPatientSeries/
/// ScoreNoValidDosesPatientSeries each already return the full summed total for their table, so
/// there's no separate summing step to implement here. SELECTBEST-2 picks the highest score,
/// tie-breaking by the best (lowest-numbered) seriesPreference.
///
/// REAL BUG, FOUND AND FIXED - found via all 18 real MenB conformance failures at once, traced
/// to one root cause. Real corpus case 2024-0032 (20-year-old, zero MenB doses) has 4 relevant,
/// equally-scored "Shared Clinical Decision Making" series, none of which has a seriesPreference
/// at all (confirmed real data: SCDM series genuinely have no ranking, "preference ranking
/// doesn't apply" per SeriesGroupInfo's own doc comment - 12 of 143 real series, not a parsing
/// gap). The PREVIOUS version of this function returned null whenever no tied top-scorer had ANY
/// seriesPreference to compare - which cascaded silently all the way up:
/// SelectPrioritizedPatientSeriesForGroup returned null for the group, DetermineBestPatientSeries-
/// ForAntigen never added anything for Meningococcal B, and the entire vaccine group vanished
/// from the final VaccineGroupForecasts list without any error - confirmed via a real pipeline
/// trace (MeningococcalBInvestigationTests).
///
/// Confirmed as a genuine bug, not spec ambiguity, by §8.8's own text: "This step only happens
/// after ONE prioritized patient series has been selected for each Series Group for the
/// antigen" - the spec's own framing assumes §8.7 always produces exactly one winner per group,
/// never zero. SELECTBEST-2's two named bullets (highest score, then best-ranked preference)
/// don't specify a further tie-break for when preference data is uniformly absent among the tied
/// candidates, but §8.8's own precondition means one must still be chosen - the previous "give
/// up" behavior contradicted the very next section's stated assumption.
///
/// Fixed by falling back to a deterministic choice (ordered by series name, for
/// reproducibility) whenever the tie survives BOTH the score comparison and the seriesPreference
/// comparison - whether because no tied candidate has a preference at all (this case) or because
/// multiple candidates are still tied even after comparing preference (the pre-existing
/// `preferenceWinners.Length == 1 ? ... : null` case, unified into the same fallback here since
/// the same §8.8 reasoning applies equally to both).
/// </summary>
public static class SelectPrioritizedPatientSeries
{
    /// <returns>The prioritized series, or null only for a genuinely empty input - every non-empty group now resolves to exactly one winner, per §8.8's own stated precondition.</returns>
    public static AntigenSeries? Execute(IReadOnlyList<ScoredPatientSeries> scoredSeries)
    {
        if (scoredSeries.Count == 0)
        {
            return null;
        }

        var maxScore = scoredSeries.Max(s => s.Score);
        var topScorers = scoredSeries.Where(s => s.Score == maxScore).ToArray();
        if (topScorers.Length == 1)
        {
            return topScorers[0].Series;
        }

        // Tie-break: best (lowest-numbered) seriesPreference among the tied series. A tied
        // series with no seriesPreference at all can't participate in this comparison (nothing
        // to rank it by), so it's excluded rather than treated as automatically best or worst.
        var withPreference = topScorers.Where(s => s.Series.SeriesGroupInfo.SeriesPreference is not null).ToArray();

        IReadOnlyList<ScoredPatientSeries> stillTied;
        if (withPreference.Length == 0)
        {
            // No tied candidate has a seriesPreference at all - the comparison above has nothing
            // to work with, not just an ordinary loss within it. Fall through to the same
            // deterministic tie-break as below, on the full set of top scorers.
            stillTied = topScorers;
        }
        else
        {
            var bestPreference = withPreference.Min(s => s.Series.SeriesGroupInfo.SeriesPreference!.Value);
            stillTied = withPreference.Where(s => s.Series.SeriesGroupInfo.SeriesPreference == bestPreference).ToArray();
        }

        if (stillTied.Count == 1)
        {
            return stillTied[0].Series;
        }

        // Neither SELECTBEST-2's score comparison nor its seriesPreference tie-break fully
        // resolves this - see this class's own doc comment for why a deterministic fallback,
        // rather than giving up, is the spec-consistent choice: §8.8's own precondition requires
        // exactly one winner per group.
        return stillTied.OrderBy(s => s.Series.SeriesName, StringComparer.Ordinal).First().Series;
    }
}
