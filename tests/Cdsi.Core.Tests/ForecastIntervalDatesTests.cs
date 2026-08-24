/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class ForecastIntervalDatesTests
{
    // Real data: "HepB 3-dose series" Dose 3 - two interval groups:
    //   fromPrevious:            minInt "8 weeks", earliestRecInt "8 weeks", latestRecInt "18 months + 4 weeks"
    //   fromTargetDose (Dose 1): minInt "16 weeks", earliestRecInt/latestRecInt both EMPTY
    private static IReadOnlyList<PreferableIntervalRule> HepBDose3Intervals =>
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"))
            .Single(s => s.SeriesName == "HepB 3-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 3).PreferableIntervals;

    private static readonly Dictionary<Cdsi.Core.ReferenceData.IntervalReferenceType, DateOnly> ReferenceDates = new()
    {
        [IntervalReferenceType.FromPrevious] = new DateOnly(2020, 3, 1),   // Dose 2
        [IntervalReferenceType.FromTargetDose] = new DateOnly(2020, 1, 1)  // Dose 1
    };

    private static DateOnly? Resolve(PreferableIntervalRule rule) => ReferenceDates.TryGetValue(rule.ReferenceType, out var d) ? d : null;

    [Fact]
    public void LatestMinIntervalDate_TakesMaxAcrossBothReferenceGroups()
    {
        // fromPrevious: 2020-03-01 + 8 weeks = 2020-04-26. fromTargetDose: 2020-01-01 + 16 weeks = 2020-04-22.
        var result = ForecastIntervalDates.LatestMinIntervalDate(new DateOnly(2020, 6, 1), HepBDose3Intervals, Resolve);

        Assert.Equal(new DateOnly(2020, 4, 26), result);
    }

    [Fact]
    public void LatestEarliestRecIntervalDate_OnlyFromPreviousGroupContributes()
    {
        // fromTargetDose group has no earliestRecInt at all - excluded, not treated as a
        // sentinel. Only fromPrevious (2020-03-01 + 8 weeks = 2020-04-26) contributes.
        var result = ForecastIntervalDates.LatestEarliestRecIntervalDate(new DateOnly(2020, 6, 1), HepBDose3Intervals, Resolve);

        Assert.Equal(new DateOnly(2020, 4, 26), result);
    }

    [Fact]
    public void LatestLatestRecIntervalDate_OnlyFromPreviousGroupContributes()
    {
        // 2020-03-01 + (18 months + 4 weeks) = 2021-09-01 + 28 days = 2021-09-29.
        var result = ForecastIntervalDates.LatestLatestRecIntervalDate(new DateOnly(2020, 6, 1), HepBDose3Intervals, Resolve);

        Assert.Equal(new DateOnly(2021, 9, 29), result);
    }

    [Fact]
    public void NoRules_ReturnsNull()
    {
        var result = ForecastIntervalDates.LatestMinIntervalDate(new DateOnly(2020, 6, 1), Array.Empty<PreferableIntervalRule>(), Resolve);

        Assert.Null(result);
    }

    [Fact]
    public void NoResolvableReferenceDateForAnyGroup_ReturnsNull()
    {
        var result = ForecastIntervalDates.LatestMinIntervalDate(new DateOnly(2020, 6, 1), HepBDose3Intervals, _ => null);

        Assert.Null(result);
    }
}
