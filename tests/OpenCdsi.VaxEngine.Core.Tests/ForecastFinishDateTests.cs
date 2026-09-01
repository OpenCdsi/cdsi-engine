/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using OpenCdsi.VaxEngine.Core.Evaluation;
using OpenCdsi.VaxEngine.Core.ReferenceData;
using Xunit;

namespace OpenCdsi.VaxEngine.Core.Tests;

public class ForecastFinishDateTests
{
    // Real data: "HepB 3-dose series" Dose 3 has two interval groups with minInt "8 weeks" and
    // "16 weeks" respectively (used throughout this project's Interval work).
    private static SeriesDose HepBDose3 =>
        AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("HepB"))
            .Single(s => s.SeriesName == "HepB 3-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 3);

    [Fact]
    public void RealHepBDose3_TakesLatestOfBothIntervalGroups()
    {
        var earliestDate = new DateOnly(2024, 1, 1);

        // fromPrevious minInt "8 weeks" -> 2024-02-26; fromTargetDose minInt "16 weeks" -> 2024-04-22.
        // The latter is later, so it should win.
        var result = ForecastFinishDate.Calculate(earliestDate, new[] { HepBDose3 });

        Assert.Equal(new DateOnly(2024, 4, 22), result);
    }

    [Fact]
    public void NoRemainingDoses_ReturnsEarliestDateUnchanged()
    {
        var earliestDate = new DateOnly(2024, 1, 1);

        var result = ForecastFinishDate.Calculate(earliestDate, Array.Empty<SeriesDose>());

        Assert.Equal(earliestDate, result);
    }

    [Fact]
    public void DoseWithNoIntervalRulesAtAll_DoesNotContributeAnyCandidateDate()
    {
        var earliestDate = new DateOnly(2024, 1, 1);
        // "HepB 3-dose series" Dose 1 has no interval rules at all (no previous dose to measure from).
        var dose1 = AntigenSupportingDataLoader.LoadFile(TestPaths.AntigenFile("HepB"))
            .Single(s => s.SeriesName == "HepB 3-dose series")
            .SeriesDoses.Single(d => d.DoseNumber == 1);

        var result = ForecastFinishDate.Calculate(earliestDate, new[] { dose1 });

        Assert.Equal(earliestDate, result); // falls back to earliestDate since no candidates exist
    }
}
