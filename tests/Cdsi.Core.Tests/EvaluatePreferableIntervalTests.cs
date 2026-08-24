/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class EvaluatePreferableIntervalTests
{
    private static readonly IReadOnlyList<AntigenSeries> HepBSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"));

    // Real data: "HepB 3-dose series" Dose 3 - two interval groups that must BOTH be satisfied:
    //   fromPrevious:              absMinInt "8 weeks - 4 days", minInt "8 weeks"
    //   fromTargetDose (Dose 1):   absMinInt "16 weeks - 4 days", minInt "16 weeks"
    private static IReadOnlyList<PreferableIntervalRule> HepBDose3Intervals =>
        HepBSeries.Single(s => s.SeriesName == "HepB 3-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 3).PreferableIntervals;

    [Fact]
    public void NoRulesSpecified_IsAlwaysValid()
    {
        // §6.5: "In cases where a target dose does not specify preferable interval attributes,
        // the interval is considered 'valid.'"
        var result = EvaluatePreferableInterval.Execute(
            new DateOnly(2020, 1, 1), Array.Empty<PreferableIntervalRule>(), _ => null);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void GroupByReferencePoint_SplitsFromPreviousAndFromTargetDoseIntoSeparateGroups()
    {
        var groups = EvaluatePreferableInterval.GroupByReferencePoint(HepBDose3Intervals).ToArray();

        Assert.Equal(2, groups.Length);
        Assert.Contains(groups, g => g[0].ReferenceType == IntervalReferenceType.FromPrevious);
        Assert.Contains(groups, g => g[0].ReferenceType == IntervalReferenceType.FromTargetDose && g[0].ReferenceTargetDoseNumber == 1);
    }

    [Fact]
    public void BothReferenceGroupsSatisfied_IsValid()
    {
        // Dose 2 given 2020-03-01, Dose 1 given 2020-01-01, Dose 3 given 2020-06-01 -
        // the exact scenario we hand-traced in the design conversation. Both groups pass well
        // past their minInt thresholds.
        var referenceDates = new Dictionary<IntervalReferenceType, DateOnly>
        {
            [IntervalReferenceType.FromPrevious] = new DateOnly(2020, 3, 1),   // Dose 2
            [IntervalReferenceType.FromTargetDose] = new DateOnly(2020, 1, 1)  // Dose 1
        };

        var result = EvaluatePreferableInterval.Execute(
            new DateOnly(2020, 6, 1), HepBDose3Intervals, rule => referenceDates[rule.ReferenceType]);

        Assert.True(result.IsValid);
        Assert.Null(result.Reason);
    }

    [Fact]
    public void OneReferenceGroupFails_OverallOutcomeIsNotValid_EvenIfTheOtherPasses()
    {
        // The "AND across reference points" behavior we flagged as an easy-to-miss trap:
        // Dose 1 was only 2 weeks before this Dose 3 (fromTargetDose group fails, well under
        // its 16-week absolute minimum), even though the fromPrevious group (Dose 2, 10 weeks
        // prior) comfortably passes on its own.
        var referenceDates = new Dictionary<IntervalReferenceType, DateOnly>
        {
            [IntervalReferenceType.FromPrevious] = new DateOnly(2020, 3, 21),   // 10 weeks before administration
            [IntervalReferenceType.FromTargetDose] = new DateOnly(2020, 5, 15)  // only 2 weeks before administration
        };

        var result = EvaluatePreferableInterval.Execute(
            new DateOnly(2020, 5, 29), HepBDose3Intervals, rule => referenceDates[rule.ReferenceType]);

        Assert.False(result.IsValid);
        Assert.Equal("Too soon", result.Reason);
    }

    [Fact]
    public void GracePeriod_BetweenAbsoluteMinimumAndMinimum_IsValidWithGracePeriodReason()
    {
        // fromPrevious group alone: absMinIntervalDate = ref + (8wk - 4d), minIntervalDate = ref + 8wk.
        var rule = HepBDose3Intervals.Single(r => r.ReferenceType == IntervalReferenceType.FromPrevious);
        var referenceDate = new DateOnly(2020, 1, 1);
        // absMinIntervalDate = 2020-01-01 + 52 days = 2020-02-22; minIntervalDate = 2020-01-01 + 56 days = 2020-02-26
        var dateAdministered = new DateOnly(2020, 2, 24); // inside the grace window

        var result = EvaluatePreferableInterval.EvaluateSingleRule(dateAdministered, referenceDate, rule);

        Assert.True(result.IsValid);
        Assert.Equal("Grace period", result.Reason);
    }

    [Fact]
    public void NullReferenceDate_TreatedAsSatisfied()
    {
        // No resolvable reference event for this rule (e.g. no matching prior dose exists) -
        // per our design, that particular constraint doesn't apply rather than failing closed.
        var rule = HepBDose3Intervals.Single(r => r.ReferenceType == IntervalReferenceType.FromPrevious);

        var result = EvaluatePreferableInterval.EvaluateSingleRule(new DateOnly(2020, 1, 1), null, rule);

        Assert.True(result.IsValid);
    }
}
