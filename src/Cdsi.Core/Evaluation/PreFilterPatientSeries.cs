using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>What §8.1 needs to know about one relevant patient series being considered for a series group.</summary>
public sealed record ScorableSeriesCandidate(
    AntigenSeries Series,
    bool IsContraindicated,
    PatientSeriesStatus PatientSeriesStatus,
    int ValidDoseCount,
    DateOnly? EarliestValidDoseDate);

/// <summary>
/// §8.1 Pre-Filter Patient Series (Table 8-2: SELECTB-24, SELECTSCORE-2). Determines which
/// relevant patient series in a series group should be considered "scorable" at all, before
/// §8.2-8.7's actual scoring logic runs.
/// </summary>
public static class PreFilterPatientSeries
{
    /// <summary>SELECTB-24: a contraindicated series is still a candidate UNLESS every series in its group is also contraindicated (in which case none of them being scorable would leave nothing to recommend, so the contraindication doesn't disqualify it here).</summary>
    public static bool IsCandidateScorablePatientSeries(bool isContraindicated, IReadOnlyList<bool> contraindicatedStatusesInGroup)
    {
        if (!isContraindicated)
        {
            return true;
        }
        return contraindicatedStatusesInGroup.All(c => c);
    }

    /// <summary>
    /// SELECTSCORE-2's four OR'd bullets. Bullet 4 (Evaluation Only + complete) is checked
    /// independently of candidate-scorable status per the spec's own bullet structure - it has
    /// no "is a candidate scorable patient series" condition, unlike bullets 1-3.
    /// </summary>
    public static bool IsScorablePatientSeries(
        ScorableSeriesCandidate candidate,
        bool isCandidateScorable,
        IReadOnlyList<ScorableSeriesCandidate> allCandidatesInGroup,
        DateOnly dateOfBirth)
    {
        // Bullet 4.
        if (candidate.Series.SeriesType == SeriesType.EvaluationOnly &&
            ClassifyScorablePatientSeries.IsCompletePatientSeries(candidate.PatientSeriesStatus))
        {
            return true;
        }

        if (!isCandidateScorable)
        {
            return false;
        }

        // Bullet 1: Risk series with priority as good as or better than every series in the group
        // ("A" is highest priority; ordinal string comparison matches directly).
        if (candidate.Series.SeriesType == SeriesType.Risk)
        {
            var thisPriority = candidate.Series.SeriesGroupInfo.SeriesPriority;
            var isHighestOrTied = allCandidatesInGroup.All(other =>
                string.CompareOrdinal(thisPriority, other.Series.SeriesGroupInfo.SeriesPriority) <= 0);
            if (isHighestOrTied)
            {
                return true;
            }
        }

        // Bullet 2: Standard series with at least one valid dose, given before the series' own
        // maximum age to start. INFERENCE: no maxAgeToStart at all is treated as unbounded
        // (always satisfied), consistent with how an absent age ceiling is handled elsewhere in
        // this codebase (e.g. §6.4's own maxAge default).
        if (candidate.Series.SeriesType == SeriesType.Standard &&
            candidate.ValidDoseCount > 0 && candidate.EarliestValidDoseDate is DateOnly earliestValidDate)
        {
            var maxAgeToStartDate = candidate.Series.SeriesGroupInfo.MaxAgeToStartDate(dateOfBirth);
            if (maxAgeToStartDate is null || earliestValidDate < maxAgeToStartDate.Value)
            {
                return true;
            }
        }

        // Bullet 3: Standard series where the WHOLE group has zero valid doses and no default series exists in the group.
        if (candidate.Series.SeriesType == SeriesType.Standard)
        {
            var allZeroValid = allCandidatesInGroup.All(c => c.ValidDoseCount == 0);
            var noDefaultInGroup = allCandidatesInGroup.All(c => !c.Series.SeriesGroupInfo.IsDefaultSeries);
            if (allZeroValid && noDefaultInGroup)
            {
                return true;
            }
        }

        return false;
    }
}
