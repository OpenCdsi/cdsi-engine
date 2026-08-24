/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Cdsi.Core.ReferenceData;
using Xunit;

namespace Cdsi.Core.Tests;

public class EvaluateAllowableIntervalTests
{
    private static readonly IReadOnlyList<AntigenSeries> HepBSeries =
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("AntigenSupportingData-_HepB-508.xml"));

    // Real data: "HepB 3-dose series" Dose 3 has NO allowableInterval rules at all (empty
    // placeholder in the source XML) - the exact case §6.6 says must default to "not valid".
    private static IReadOnlyList<AllowableIntervalRule> HepBDose3AllowableIntervals =>
        HepBSeries.Single(s => s.SeriesName == "HepB 3-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 3).AllowableIntervals;

    // Real data: "HepB Heplisav-B 2-dose series" Dose 2 - fromTargetDose 1, absMinInt "4 weeks - 4 days".
    private static IReadOnlyList<AllowableIntervalRule> HeplisavDose2AllowableIntervals =>
        HepBSeries.Single(s => s.SeriesName == "HepB Heplisav-B 2-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 2).AllowableIntervals;

    [Fact]
    public void NoRulesSpecified_IsNotValid_UnlikePreferableIntervalsOppositeDefault()
    {
        // §6.6's explicit "to avoid a false validation" rule - confirmed against a real
        // seriesDose that genuinely has zero allowableInterval elements.
        Assert.Empty(HepBDose3AllowableIntervals); // sanity check on the fixture itself

        var result = EvaluateAllowableInterval.Execute(
            new DateOnly(2020, 6, 1), HepBDose3AllowableIntervals, _ => null);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void BeforeAbsoluteMinimumInterval_IsNotValid()
    {
        // absMinIntervalDate = referenceDate + (4 weeks - 4 days) = referenceDate + 24 days.
        var referenceDate = new DateOnly(2020, 1, 1); // Dose 1
        var dateAdministered = new DateOnly(2020, 1, 20); // 19 days later - before the 24-day floor

        var result = EvaluateAllowableInterval.Execute(
            dateAdministered, HeplisavDose2AllowableIntervals, _ => referenceDate);

        Assert.False(result.IsValid);
        Assert.Equal("Too soon", result.Reason);
    }

    [Fact]
    public void AtOrAfterAbsoluteMinimumInterval_IsValid()
    {
        var referenceDate = new DateOnly(2020, 1, 1);
        var dateAdministered = new DateOnly(2020, 1, 25); // 24 days later - exactly at the floor

        var result = EvaluateAllowableInterval.Execute(
            dateAdministered, HeplisavDose2AllowableIntervals, _ => referenceDate);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void NullReferenceDate_TreatedAsSatisfied()
    {
        var rule = HeplisavDose2AllowableIntervals.Single();

        var result = EvaluateAllowableInterval.EvaluateSingleRule(new DateOnly(2020, 1, 1), null, rule);

        Assert.True(result.IsValid);
    }
}
