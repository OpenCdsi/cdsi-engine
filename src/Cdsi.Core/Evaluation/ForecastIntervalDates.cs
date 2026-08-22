using Cdsi.Core.Common;
using Cdsi.Core.ReferenceData;

namespace Cdsi.Core.Evaluation;

/// <summary>
/// Computes the "latest of all X interval dates" inputs §7.5 needs (CALCDTINT-4/5/6), reusing
/// EvaluatePreferableInterval's existing reference-point grouping and temporal selection rather
/// than reimplementing it. Each reference-point group contributes at most one date (per its
/// temporally-applicable rule instance); the result is the latest across all groups that have a
/// resolvable reference date, or null if none do.
/// </summary>
public static class ForecastIntervalDates
{
    public static DateOnly? LatestMinIntervalDate(
        DateOnly anchorDate, IReadOnlyList<PreferableIntervalRule> rules, Func<PreferableIntervalRule, DateOnly?> resolveReferenceDate) =>
        LatestAcrossGroups(anchorDate, rules, resolveReferenceDate, (rule, refDate) => rule.MinInt?.AddTo(refDate));

    public static DateOnly? LatestEarliestRecIntervalDate(
        DateOnly anchorDate, IReadOnlyList<PreferableIntervalRule> rules, Func<PreferableIntervalRule, DateOnly?> resolveReferenceDate) =>
        LatestAcrossGroups(anchorDate, rules, resolveReferenceDate, (rule, refDate) => rule.EarliestRecIntDate(refDate));

    public static DateOnly? LatestLatestRecIntervalDate(
        DateOnly anchorDate, IReadOnlyList<PreferableIntervalRule> rules, Func<PreferableIntervalRule, DateOnly?> resolveReferenceDate) =>
        LatestAcrossGroups(anchorDate, rules, resolveReferenceDate, (rule, refDate) => rule.LatestRecIntDate(refDate));

    private static DateOnly? LatestAcrossGroups(
        DateOnly anchorDate,
        IReadOnlyList<PreferableIntervalRule> rules,
        Func<PreferableIntervalRule, DateOnly?> resolveReferenceDate,
        Func<PreferableIntervalRule, DateOnly, DateOnly?> computeDate)
    {
        DateOnly? latest = null;
        foreach (var group in EvaluatePreferableInterval.GroupByReferencePoint(rules))
        {
            var applicable = TemporalRuleSelector.SelectApplicable(group, anchorDate);
            var referenceDate = resolveReferenceDate(applicable);
            if (referenceDate is not DateOnly refDate)
            {
                continue;
            }
            var candidate = computeDate(applicable, refDate);
            if (candidate is DateOnly d && (latest is null || d > latest))
            {
                latest = d;
            }
        }
        return latest;
    }
}
