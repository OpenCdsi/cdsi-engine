/* This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/. */

using Cdsi.Core.Evaluation;
using Xunit;

namespace Cdsi.Core.Tests;

public class ScoreCompletePatientSeriesTests
{
    [Fact]
    public void UniqueMostValidDoses_ScoresPlusOne()
    {
        var result = ScoreCompletePatientSeries.Execute(thisSeriesValidDoseCount: 4, allValidDoseCountsInGroup: new[] { 4, 3, 2 });
        Assert.Equal(1, result);
    }

    [Fact]
    public void TiedForMostValidDoses_ScoresZero()
    {
        var result = ScoreCompletePatientSeries.Execute(thisSeriesValidDoseCount: 4, allValidDoseCountsInGroup: new[] { 4, 4, 2 });
        Assert.Equal(0, result);
    }

    [Fact]
    public void NotTheMostValidDoses_ScoresMinusOne()
    {
        var result = ScoreCompletePatientSeries.Execute(thisSeriesValidDoseCount: 2, allValidDoseCountsInGroup: new[] { 4, 3, 2 });
        Assert.Equal(-1, result);
    }

    [Fact]
    public void OnlyOneSeriesInGroup_AlwaysScoresPlusOne()
    {
        var result = ScoreCompletePatientSeries.Execute(thisSeriesValidDoseCount: 3, allValidDoseCountsInGroup: new[] { 3 });
        Assert.Equal(1, result);
    }

    [Fact]
    public void AllTiedAtSameCount_EveryoneScoresZero()
    {
        var counts = new[] { 3, 3, 3 };
        var results = counts.Select(c => ScoreCompletePatientSeries.Execute(c, counts));
        Assert.All(results, r => Assert.Equal(0, r));
    }
}
