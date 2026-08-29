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
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("HepB"))
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

    [Fact]
    public void DiagnosticOnly_RealPertussisDose8_LatestMinIntervalDate_InIsolation()
    {
        // DIAGNOSTIC, not a fix. Real Dose 8 of the Pertussis standard series has one interval
        // group: FromPrevious, minInt "4 weeks". Testing this function completely in isolation -
        // no loop, no merge, no other pipeline layer - to check a specific, narrow hypothesis
        // after a full-pipeline diagnostic produced a result none of several hand-traces could
        // explain (see GeneratePatientSeriesForecastTests's own diagnostics). A resolver that
        // simply always returns the one real administered date (2026-08-05, this patient's only
        // dose) for FromPrevious should - per this function's own already-tested, already-correct
        // behavior on real HepB data above - produce 2026-08-05 + 4 weeks = 2026-09-02. If this
        // comes back anything else, the bug is genuinely inside this function or how Dose 8's own
        // PreferableIntervals load from the real XML, not in the re-forecast loop or the merge.
        var pertussisDose8Intervals = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("Pertussis"))
            .Single(s => s.SeriesName == "Pertussis standard series")
            .SeriesDoses.Single(d => d.DoseNumber == 8).PreferableIntervals;

        DateOnly? Resolve(PreferableIntervalRule rule) => rule.ReferenceType == IntervalReferenceType.FromPrevious ? new DateOnly(2026, 8, 5) : null;

        var result = ForecastIntervalDates.LatestMinIntervalDate(new DateOnly(2026, 8, 5), pertussisDose8Intervals, Resolve);

        Assert.Equal(new DateOnly(2026, 9, 2), result);
    }
}
