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
/// </summary>
public static class SelectPrioritizedPatientSeries
{
    /// <returns>The prioritized series, or null if no single winner can be determined (empty input, or a tie that persists even after the seriesPreference tie-break - not itself a case the spec's two named rules resolve further).</returns>
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
        if (withPreference.Length == 0)
        {
            return null;
        }

        var bestPreference = withPreference.Min(s => s.Series.SeriesGroupInfo.SeriesPreference!.Value);
        var preferenceWinners = withPreference.Where(s => s.Series.SeriesGroupInfo.SeriesPreference == bestPreference).ToArray();

        return preferenceWinners.Length == 1 ? preferenceWinners[0].Series : null;
    }
}
